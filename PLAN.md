# TorrentWatcher — план реализации

Статус: план выполнен (TW-01–TW-40).
Следующий пункт: **нет — все задачи закрыты**.
Версия плана: 8 (2026-11-21).

---

## Как ведётся этот план

Правила обязательны для всех, кто вносит изменения в проект (людей и агентов).

1. **Один пункт = одна задача `TW-xx`.** Работаем по порядку, если не сказано иначе.
2. **Выполненный пункт не удаляется, а вычёркивается.** Меняем `- [ ]` на `- [x]`, весь текст
   оборачиваем в `~~...~~` и дописываем в конец `— done YYYY-MM-DD`. Пример:
   `- [x] ~~TW-01. Solution и каркас проектов — done 2026-11-18~~`
3. **Строку «Следующий пункт» в шапке обновляем** сразу после того, как вычеркнули предыдущий.
4. **Корректировки плана — это нормально.** Любое изменение формулировки, порядка, состава
   задач или оценок фиксируется в разделе «Журнал изменений плана»: дата, что было (зачёркнуто),
   что стало, почему. Старый текст не удаляется.
5. **Не переписываем историю задним числом.** Оценки выполненных пунктов не пересчитываем.
6. **Отклонение от плана по ходу работы** — тоже запись в журнал, а не молчаливая правка.

---

## 1. Цель и скоуп

Небольшое self-hosted приложение: хранит список топиков рутрекера, периодически скачивает
актуальный `.torrent` по неизменной ссылке `dl.php?t={id}` и определяет, обновился ли он
(по infohash). При обновлении фиксирует дату и показывает статус в UI.

### Входит в скоуп

- CRUD списка отслеживаемых торрентов (ссылка на топик).
- Глобальная кука рутрекера, задаваемая в UI, с проверкой.
- Периодический автоскан с глобальным интервалом, применяемым без перезапуска.
- Форс-проверка одного торрента и всех сразу.
- Определение изменения по infohash; сохранение только последнего `.torrent`-файла.
- Применение обновлений в qBittorrent (Web UI API v2): замена раздачи с сохранением папки,
  категории, тегов и уже скачанных файлов; авто-применение и ручная кнопка.
- Web UI на Vue 3 + TS, API на ASP.NET Core (net10), SQLite + EF Core, Docker.

### Не входит в скоуп (сейчас)

- ~~Интеграция с qBittorrent (только задел в виде порта и статуса «обнаружено обновление»).~~
  → реализована в TW-39 (этап 12).
- Уведомления (Telegram/ntfy/e-mail).
- История обновлений и любые журналы событий в БД.
- Авторизация, мультипользовательность, несколько кук.
- Интервал на отдельный торрент (только глобальный).
- Хранение нескольких версий `.torrent`.

---

## 2. Принятые решения

| Вопрос | Решение |
|---|---|
| Стек | .NET 10 (SDK 10.0.401), ASP.NET Core MVC-контроллеры, EF Core 10, SQLite |
| Слои | `Domain` → `Application` → `Infrastructure` / `Web` (Web знает Infrastructure только для DI) |
| Кука рутрекера | ~~одна глобальная, в таблице настроек, plaintext~~ → одна глобальная пара логин/пароль; пароль шифруется Data Protection (ключи вне БД), кука сеанса кэшируется в настройках и профиле Chromium; ручная кука — аварийный вариант |
| Период | глобальный, минуты, 1…1440, применяется на лету |
| Заголовок торрента | `name` из info-словаря `.torrent`; HTML рутрекера не парсим |
| Хранение `.torrent` | только последний файл: `/data/torrents/{topicId}.torrent` |
| История | не храним; «дата последнего обновления» = `TrackedTorrent.LastChangedAtUtc` |
| Статус скана | in-memory singleton (без таблицы) |
| Планировщик | один `BackgroundService` с `while(true)` + `Task.Delay`, без Quartz/Hangfire |
| UI-фреймворк | PrimeVue 4 (MIT) + тема Aura, иконки PrimeIcons |
| Обнаружено обновление | статус `UpdateAvailable` + дата; ничего не качаем |
| qBittorrent | API v2 Web UI: старая раздача удаляется без файлов, новая добавляется в ту же папку (`savepath`/категория/теги старой), затем recheck + resume; авто-применение и ручная кнопка |
| Cloudflare на `dl.php` | managed challenge обходится Playwright ради `cf_clearance`; `.torrent` качает `HttpClient`; ручной режим — fallback |
| Авторизация | нет; слушать LAN/localhost, предупреждение в README |

---

## 3. Структура решения

```
C:\Work\TorrentWatcher\
├─ TorrentWatcher.slnx
├─ PLAN.md / AGENTS.md / README.md
├─ Directory.Build.props            # Nullable, ImplicitUsings, TreatWarningsAsErrors
├─ global.json                      # 10.0.401, rollForward latestFeature
├─ .editorconfig / .gitignore / .dockerignore
├─ docker-compose.yml
├─ src\
│  ├─ TorrentWatcher.Domain\        # сущности, enum'ы, чистая логика (URL, bencode)
│  ├─ TorrentWatcher.Application\   # use-cases, порты, DTO, валидация
│  ├─ TorrentWatcher.Infrastructure\# EF Core + SQLite, HttpClient, файловое хранилище
│  └─ TorrentWatcher.Web\           # Program.cs, контроллеры, ProblemDetails, wwwroot
│     └─ ClientApp\                 # Vue 3 + TS + Vite + PrimeVue
└─ tests\
   ├─ TorrentWatcher.UnitTests\
   └─ TorrentWatcher.IntegrationTests\
```

Ключевые файлы по слоям:

- `Domain`: `Entities/{TrackedTorrent,AppSetting}.cs`, `Parsing/{RutrackerUrlParser,BencodeReader,TorrentMetadata}.cs`
- `Application`: `Abstractions/*` (порты), `Services/{TorrentService,TorrentScanService,SettingsService,ScanCoordinator,RutrackerSessionService}.cs`,
  `State/ScanStatus.cs`, `Dtos/*`, `Exceptions/AppException.cs`
- `Infrastructure`: `Persistence/AppDbContext.cs`, `Persistence/Configurations/*`, `Persistence/Migrations/*`,
  `Rutracker/RutrackerClient.cs`, `Rutracker/PlaywrightChallengeSolver.cs`, `Security/SecretProtector.cs`,
  `Qbittorrent/QbittorrentClient.cs`, `Storage/TorrentFileStorage.cs`
- `Web`: `Program.cs`, `Controllers/{Torrents,Settings,System}Controller.cs`, `Contracts/*`,
  `Middleware/ExceptionHandlingMiddleware.cs`, `wwwroot/*`, `ClientApp/*`

Применение обновлений в qBittorrent: `Application/Services/TorrentApplyService.cs`
(порт `ITorrentUpdateApplier`), реализация API v2 — `Infrastructure/Qbittorrent/QbittorrentClient.cs`.

---

## 4. Модель данных

SQLite, две таблицы. WAL, `busy_timeout=5000`, `foreign_keys=on`, миграции накатываются на старте.

**TrackedTorrent**

| Поле | Тип | Примечание |
|---|---|---|
| Id | INTEGER PK | |
| TopicId | INTEGER | unique index |
| TopicUrl | TEXT | канонический `viewtopic.php?t=` |
| Title | TEXT | `name` из info-словаря |
| InfoHashV1 | TEXT(40) | hex, текущий известный |
| TorrentSizeBytes / FileCount | INTEGER | из метаданных |
| TorrentFilePath | TEXT | `/data/torrents/{topicId}.torrent` |
| TorrentFileSha256 | TEXT | сырые байты файла |
| TorrentFileDownloadedAtUtc | DATETIME | |
| LastCheckedAtUtc | DATETIME? | |
| LastCheckOutcome | INTEGER | `Never/Unchanged/Changed/Error` |
| LastError | TEXT? | текст последней ошибки |
| LastChangedAtUtc | DATETIME? | **дата последнего обновления** |
| IsEnabled | INTEGER | пауза для отдельного торрента |
| CreatedAtUtc / UpdatedAtUtc | DATETIME | |

**AppSetting** (одна строка, Id = 1)

| Поле | Примечание |
|---|---|
| ~~RutrackerCookie~~ → RutrackerLogin | ~~`bb_session=...; bb_data=...`~~ логин рутрекера |
| RutrackerPasswordProtected | пароль рутрекера, шифротекст Data Protection; в API и журнал не попадает |
| RutrackerCookie | кэш куки сеанса (`bb_session=...; bb_data=...`), заполняется после входа; ручной fallback |
| CookieVerifiedAtUtc / CookieLastError | результат последней проверки сеанса или входа |
| ScanIntervalMinutes | 1…1440 |
| AutoScanEnabled | вкл/выкл автoскан |
| RequestDelayMs | пауза между запросами, по умолчанию 1500 |
| RequestTimeoutSeconds | по умолчанию 30 |
| LastScanAtUtc | для расчёта следующего запуска после рестарта |
| UpdatedAtUtc | |

**In-memory** `ScanStatus` (singleton): `RunId`, `Trigger`, `StartedAtUtc`, `FinishedAtUtc`,
`Status (Running/Completed/Failed)`, `TorrentsTotal`, `ChangedCount`, `FailedCount`, `Message`.

---

## 5. API (`/api`)

| Метод | Путь | Назначение |
|---|---|---|
| GET | `/api/torrents` | список + сводка (кол-во, есть ли обновления) |
| POST | `/api/torrents` | `{ url }` → парсинг topicId, скачивание, сохранение; 409 при дубликате |
| DELETE | `/api/torrents/{id}` | убрать из отслеживания (файл `.torrent` удаляем, данные на диске не трогаем) |
| POST | `/api/torrents/{id}/check` | форс-проверка одного, синхронно, результат в ответе |
| POST | `/api/torrents/check-all` | 202 + `runId`; выполняется в фоне, UI опрашивает статус |
| GET/PUT | `/api/settings` | настройки; PUT применяется сразу |
| POST | `/api/settings/rutracker/login` | войти браузером по сохранённым логину и паролю |
| POST | `/api/settings/test-cookie` | пробное скачивание по первому отслеживаемому топику (проверка сеанса) |
| GET | `/api/system/status` | версия, текущий run, последний/следующий запуск, счётчики, состояние куки |
| GET | `/api/health` | healthcheck для Docker |

Ошибки — RFC 7807 `ProblemDetails`, коды: `RUTRACKER_COOKIE_INVALID`, `RUTRACKER_UNAVAILABLE`,
`TOPIC_NOT_FOUND`, `INVALID_URL`, `DUPLICATE_TOPIC`, `SCAN_ALREADY_RUNNING`, а также
`RUTRACKER_LOGIN_FAILED`, `RUTRACKER_LOGIN_CAPTCHA_REQUIRED`, `RUTRACKER_CREDENTIALS_UNREADABLE`.

---

## 6. Настройки «на лету» и планировщик

- Источник истины — строка `AppSetting` в БД. Singleton `SettingsProvider` держит снапшот и событие `Changed`.
- `PUT /api/settings` → запись в БД → обновление снапшота → событие. Перезапуск не требуется:
  кука читается из снапшота на каждый HTTP-запрос, интервал — на каждой итерации цикла и при событии.
- `ScanScheduler : BackgroundService`, внутри — обычный бесконечный цикл:

```csharp
while (!ct.IsCancellationRequested)
{
    var s = settings.Current;
    if (!s.AutoScanEnabled) { await WaitForSettingsChange(ct); continue; }

    using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
    using var _ = settings.Changed.Subscribe(__ => linked.Cancel()); // смена интервала сразу пересчитывает таймер
    try { await Task.Delay(ComputeDelay(s, lastRunAt), linked.Token); }
    catch (OperationCanceledException) { continue; }

    await coordinator.RunScanAsync(ScanTrigger.Auto, ct);
}
```

- `ScanCoordinator` — `SemaphoreSlim(1,1)`: авто- и ручной скан не пересекаются; повторный
  `check-all` возвращает уже идущий run.
- Внутри run: торренты последовательно, пауза `RequestDelayMs`, каждый в своём `try/catch`.
  Если 2–3 ответа подряд «не торрент/403» — run прерывается, кука помечается невалидной, в UI баннер.
- Graceful shutdown через `HostOptions.ShutdownTimeout` + `StopAsync`.

---

## 7. Слой рутрекера

`RutrackerClient` (typed `HttpClient`, base `https://rutracker.org`): браузерный UA, `Referer` на
страницу топика, cookie из настроек, `GET dl.php?t={id}`. Проверки: ответ начинается с `d` и
содержит `8:announce`, ограничение размера, таймаут. Ошибки классифицируются в `RutrackerException`
(`CookieInvalid`, `NotFound`, `Network`, `InvalidPayload`).

Infohash считается локально: SHA-1 по span info-словаря, поэтому «изменилось/нет» — точное
сравнение хешей, а не байтов файла.

**Вход.** ~~Куку пользователь копирует вручную~~ → в настройках хранятся логин и пароль;
пароль шифруется ASP.NET Core Data Protection, ключи лежат в `/data/keys` вне БД.
`RutrackerSessionService` при первом скачивании без кэша куки расшифровывает пароль и просит
`PlaywrightChallengeSolver.LoginAsync` открыть `login.php`, заполнить `login-form-full`
(`login_username` / `login_password`) и дождаться `bb_session`. Куки `bb_*` кэшируются
в `AppSetting.RutrackerCookie`, дальше работает обычный `HttpClient`. После неудачного входа
10 минут автоматические попытки не повторяются (защита от блокировки аккаунта), кнопка
«Войти» паузу игнорирует. Смена логина или пароля сбрасывает кэш сеанса.

**Cloudflare.** С 2026-11-20 `dl.php` закрыт managed challenge Cloudflare (`403`,
`Cf-Mitigated: challenge`). Браузер (Playwright, Chromium с persistent-профилем) используется
только чтобы получить `cf_clearance` и UA; сама загрузка остаётся на `HttpClient`. Пропуск
хранится в `AppSetting`, привязан к IP и UA, живёт около года. Headless-Chromium проверку не
проходит, поэтому в контейнере браузер запускается под Xvfb, а локально открывается окно.
При повторном челлендже после решения — код `RUTRACKER_CHALLENGE_REQUIRED`, пропуск сбрасывается,
скан прерывается без пометки куки рутрекера невалидной.

~~Задел под qBittorrent: порт `ITorrentClient` без реализации; статус `Changed` в UI трактуется как
«обновление обнаружено, применение не автоматизировано».~~
Применение в qBittorrent реализовано в TW-39 (см. `docs/qbittorrent.md`): `ITorrentClient`
реализован в Infrastructure, применение — `ITorrentUpdateApplier`/`TorrentApplyService`.

---

## 8. Frontend

- Vite + Vue 3 + TypeScript, `<script setup>`, `vue-router` (маршруты `/` и `/settings`),
  без Pinia — composables.
- PrimeVue 4 (MIT) + Aura: `DataTable`, `Button`, `InputText`, `Textarea`, `InputNumber`,
  `ToggleSwitch`, `Dialog`/`ConfirmDialog`, `Toast`, `Message`, `Tag`, PrimeIcons.
- Типизированный `api/client.ts` (fetch + разбор `ProblemDetails`), тосты ошибок, маскирование куки.
- Экран «Торренты»: форма добавления URL, таблица (название, ссылка на топик, размер,
  «обновлён», «проверен», бейдж статуса/ошибки), «Проверить» / «Проверить все», удаление
  с подтверждением, обратный отсчёт до следующего автоскана, поллинг `/api/system/status` раз в 15 с.
- Экран «Настройки»: ~~кука~~ логин и пароль, статус сеанса, «Войти», «Проверить сеанс»,
  «Забыть данные», ручная кука в «Дополнительно», интервал, автоскан, задержка.
- `npm run build` → `../wwwroot`. Dev: Vite проксирует `/api` на `http://localhost:5080`.
- Kestrel: `UseDefaultFiles` + `UseStaticFiles` (index.html — no-cache, ассеты — immutable),
  `MapControllers`, затем `/api/{**rest}` → 404, затем `MapFallbackToFile("index.html")`
  без `:nonfile` (иначе ломаются маршруты Vue).

---

## 9. Контейнер

Multi-stage: `node:24-alpine` (npm ci + build) → `sdk:10.0` (publish с готовым `wwwroot`) →
`aspnet:10.0-noble`. Non-root, `EXPOSE 8080`, `VOLUME /data`, `HEALTHCHECK` на `/api/health`.

Compose: ~~`8080:8080`~~ `${APP_PORT:-8088}:8080` (8080 на хосте занят qBittorrent),
`./data:/data`, `ConnectionStrings__Default=Data Source=/data/torrentwatcher.db`,
`Security__KeysDirectory=/data/keys`, `TZ=Europe/Moscow`, `restart: unless-stopped`.

---

## 10. Задачи

Оценки — ориентировочные, в часах. Порядок выполнения — сверху вниз.

### Этап 0. Каркас (2 ч)

- [x] ~~TW-01. Solution, 4 проекта + 2 тестовых, ссылки между слоями, `Directory.Build.props`,
      `global.json`, `.editorconfig`, `.gitignore`, `git init`. Готово: `dotnet build` зелёный,
      зависимости слоёв не нарушены. — done 2026-11-17~~

### Этап 1. Парсинг (4 ч)

- [x] ~~TW-02. `RutrackerUrlParser`: `viewtopic.php?t=`, `#t=`, `dl.php?t=`, голый числовой id.
      Тесты на все формы + мусор. Готово: невалидный ввод даёт `INVALID_URL`. — done 2026-11-17~~
- [x] ~~TW-03. `BencodeReader` + `TorrentMetadataParser`: infohash v1, `name`, размер, число файлов,
      `private`. Тесты на фикстурах (реальный rutracker-торрент, однофайловый, юникод, битые данные).
      — done 2026-11-17~~

### Этап 2. Данные (3 ч)

- [x] ~~TW-04. `AppDbContext`, конфигурации, индексы, миграция `Initial`, сид строки настроек.
      Готово: миграция накатывается на пустую БД. — done 2026-11-17~~
- [x] ~~TW-05. Репозитории портов Application, WAL/`busy_timeout`/`foreign_keys` при старте.
      Готово: перезапуск сохраняет данные. — done 2026-11-17~~

### Этап 3. Внешний мир (4 ч)

- [x] ~~TW-06. `RutrackerClient` + `RutrackerException` + тесты на фейковом `HttpMessageHandler`
      (200 бенкод, 200 HTML, 403, таймаут, слишком большой ответ). — done 2026-11-17~~
- [x] ~~TW-07. `TorrentFileStorage`: одна версия файла, атомарная запись tmp→rename, удаление.
      Готово: после перезаписи на диске ровно один `.torrent`. — done 2026-11-17~~

### Этап 4. Приложение (8 ч)

- [x] ~~TW-08. `SettingsProvider` + `SettingsService`: чтение/запись, валидация, событие `Changed`,
      `TestCookieAsync`. Готово: интервал вне 1…1440 отклоняется. — done 2026-11-17~~
- [x] ~~TW-09. `TorrentService`: add (URL → скачивание → сохранение → дубликаты), list, delete,
      включение/пауза. — done 2026-11-17~~
- [x] ~~TW-10. `TorrentScanService`: сравнение infohash, `Changed/Unchanged/Error`, обновление
      `LastChangedAtUtc`, `LastError`, `LastCheckOutcome`. Юнит-тесты на все ветки.
      — done 2026-11-17~~
- [x] ~~TW-11. `ScanCoordinator` + `ScanStatus`: single-flight, ручной и авто-триггер, счётчики,
      прерывание по невалидной куке. Готово: второй параллельный запуск не стартует.
      — done 2026-11-17~~

### Этап 5. Web API (6 ч)

- [x] ~~TW-12. Контроллеры `Torrents/Settings/System`, DTO, валидация входа. — done 2026-11-17~~
- [x] ~~TW-13. Middleware исключений → `ProblemDetails` с кодами ошибок; без стектрейсов в ответе.
      — done 2026-11-17~~
- [x] ~~TW-14. OpenAPI (`Microsoft.AspNetCore.OpenApi`) + Scalar на `/api/docs`. — done 2026-11-17~~
- [x] ~~TW-15. Статика, SPA fallback, `/api/health`, `/api/system/status`, кэш-заголовки.
      — done 2026-11-17~~
- [x] ~~TW-16. Интеграционные тесты (`WebApplicationFactory` + временная SQLite): CRUD, 409,
      неизвестный `/api/...` отдаёт 404, а не `index.html`. — done 2026-11-17~~

### Этап 6. Планировщик (4 ч)

- [x] ~~TW-17. `ScanScheduler`: цикл, реакция на смену настроек, выключение автоскана, graceful stop,
      логирование старта/финиша run. — done 2026-11-17~~
- [x] ~~TW-18. Тесты планировщика с `TimeProvider`. Готово: смена интервала в UI меняет время
      следующего запуска без рестарта. — done 2026-11-17~~

### Этап 7. Frontend (10 ч)

- [x] ~~TW-19. Скаффолд Vite + Vue 3 + TS, PrimeVue/Aura/PrimeIcons, роутер, лейаут, тема.
      — done 2026-11-17~~
- [x] ~~TW-20. Типизированный API-клиент, обработка `ProblemDetails`, тосты, composables.
      — done 2026-11-17~~
- [x] ~~TW-21. Экран «Торренты»: таблица, добавление, удаление с подтверждением, форс-проверка
      одного/всех, бейджи статусов, поллинг, обратный отсчёт. — done 2026-11-17~~
- [x] ~~TW-22. Экран «Настройки»: кука, интервал, автоскан, задержка, сохранение без рестарта,
      кнопка проверки куки. — done 2026-11-17~~
- [x] ~~TW-23. `npm run build` → `wwwroot`, прод-проверка через Kestrel (SPA + API на одном порту).
      — done 2026-11-17~~

### Этап 8. Docker (4 ч)

- [x] ~~TW-24. `Dockerfile` multi-stage, `.dockerignore`, non-root, healthcheck. — done 2026-11-17~~
- [x] ~~TW-25. `docker-compose.yml`, volume, env, базовая инструкция в README.
      — done 2026-11-17~~
- [x] ~~TW-26. Smoke-тест: поднять, добавить торрент, перезапустить контейнер, данные на месте.
      — done 2026-11-17~~

### Этап 9. Полировка (3 ч)

- [x] ~~TW-27. README: архитектура, API, эксплуатация, ограничения (нет авторизации), `.http`-коллекция.
      — done 2026-11-17~~
- [x] ~~TW-28. Заметка «как добавить qBittorrent позже»: точка расширения `ITorrentClient`,
      какие данные уже есть. — done 2026-11-17~~

### Этап 10. Cloudflare (14 ч)

- [x] ~~TW-29. `RutrackerClient`: распознавать managed challenge Cloudflare
      (`Cf-Mitigated: challenge`, HTML-маркеры `cf_chl_opt`/`challenge-platform`) и бросать
      отдельный вид сбоя `Challenge` с кодом `RUTRACKER_CHALLENGE_REQUIRED`; тесты на 403
      с заголовком, HTML-челлендж и кастомный UA. — done 2026-11-20~~
- [x] ~~TW-30. Порт `ICloudflareChallengeSolver` + `PlaywrightChallengeSolver`: Chromium
      с persistent-профилем в `data/browser`, `--disable-blink-features=AutomationControlled`,
      без `--enable-automation`, получение `cf_clearance` и UA. Headless проверку не проходит —
      в контейнере Xvfb. — done 2026-11-20~~
- [x] ~~TW-31. `CloudflareAwareRutrackerClient` и `CloudflareClearanceService`: кука и UA
      в `AppSetting`, подстановка в запрос, один автоматический повтор после челленджа,
      сброс пропуска при повторном отказе, поддержка в `test-cookie`; юнит-тесты. — done 2026-11-20~~
- [x] ~~TW-32. API/UI: `POST /api/settings/cloudflare/solve`, ручные `cf_clearance` и UA
      в `PUT /api/settings`, статус и ошибки в настройках, кнопки «Пройти проверку Cloudflare»
      и «Убрать пропуск». — done 2026-11-20~~
- [x] ~~TW-33. Docker: Chromium Playwright + Xvfb в образе, профиль в `/data/browser`,
      `shm_size`, README/.http, запись в журнал плана. — done 2026-11-20~~

### Этап 11. Вход по логину и паролю (7 ч)

- [x] ~~TW-34. Domain/БД: `RutrackerLogin`, `RutrackerPasswordProtected`, `ISecretProtector`
      + Data Protection (ключи в `/data/keys`), миграция `AddRutrackerCredentials`,
      `Security:KeysDirectory` в appsettings/Docker/compose; тест шифрования. — done 2026-11-21~~
- [x] ~~TW-35. Application: `IRutrackerSessionProvider` и `RutrackerSessionService` (кэш куки,
      автовход, пауза 10 минут после неудачи, коды ошибок); интеграция в `TorrentService`,
      `TorrentScanService`, `SettingsService`, `CloudflareAwareRutrackerClient`,
      `ScanCoordinator`; `AsNoTracking` в репозитории настроек. — done 2026-11-21~~
- [x] ~~TW-36. Infrastructure: `IRutrackerLogin` + вход в `PlaywrightChallengeSolver`
      (форма `login-form-full`, ожидание Cloudflare, капча, ошибка входа, извлечение `bb_*`),
      регистрация в DI. — done 2026-11-21~~
- [x] ~~TW-37. API/UI: логин/пароль в настройках, статусы и кнопки «Войти» / «Проверить сеанс» /
      «Забыть данные», `POST /api/settings/rutracker/login`, ручная кука в «Дополнительно»,
      README и `.http`. — done 2026-11-21~~
- [x] ~~TW-38. Тесты: настройки и вход (unit), API (integration), шифрование; локальный smoke
      (UI, API, миграция, ключи). — done 2026-11-21~~

### Этап 12. qBittorrent (8 ч)

- [x] ~~TW-39. Интеграция с qBittorrent по API v2: настройки (адрес Web UI, логин, пароль
      шифруется Data Protection, авто-применение, проверка соединения), `QbittorrentClient`
      в Infrastructure; `TorrentApplyService` (старая раздача удаляется с `deleteFiles=false`,
      новая добавляется в ту же папку с категорией и тегами, затем recheck и resume;
      идемпотентность, повтор при сбое); поля `AppliedInfoHashV1`, `AppliedAtUtc`,
      `SupersededInfoHashV1`, `ApplyError`; эндпоинты `POST /api/settings/qbittorrent/test`
      и `POST /api/torrents/{id}/apply`; UI: карточка настроек и бейдж/кнопка в таблице;
      миграция, тесты, README и `docs/qbittorrent.md`. Готово: при смене infohash раздача
      в клиенте заменяется, а сбой клиента не делает проверку ошибочной. — done 2026-11-21~~

### Этап 13. Деплой на сервер (3 ч)

- [x] ~~TW-40. Опубликованный порт приложения сменён с 8080 на 8088
      (`${APP_PORT:-8088}:8080`, `start.bat`, README, docs) — 8080 на хосте остаётся
      за qBittorrent. Добавлены `deploy_server.bat` (сборка образа → tar → pscp → plink,
      настройки в `scripts/deploy/server/deploy.local.cfg`), шаблон
      `scripts/deploy/server/docker-compose.yaml`, `remote-deploy.sh` (compose создаётся
      один раз, chown `/data` пользователю `app`, фаервол, healthcheck, история тегов,
      очистка старых образов) и `deploy.local.cfg.example`; `.gitignore` дополнен.
      Готово: `deploy_server.bat` разворачивает образ на сервере, UI на `:8088`. — done 2026-11-21~~

~~**Итого: ~62 ч.**~~ ~~**Итого: ~69 ч.**~~ ~~**Итого: ~77 ч.**~~ **Итого: ~80 ч.**

---

## 11. Definition of Done (проекта)

- `docker compose up -d` на чистой машине поднимает приложение, UI доступен на `/`, API на `/api`.
- Логин и пароль сохраняются через UI; пароль не отдаётся в API, вход выполняется кнопкой
  и автоматически при первом скачивании; ошибка входа даёт понятный баннер.
- Торрент добавляется по ссылке на топик и сразу показывает название, размер и infohash.
- Автоскан идёт с заданным интервалом; смена интервала и куки применяется без перезапуска.
- Форс-проверка одного и всех работает; при изменении infohash обновляется дата и статус.
- Обновление применяется в qBittorrent: старая раздача исчезает, новая появляется в той же
  папке; сбой клиента виден в таблице и не ломает скан.
- Данные и `.torrent` переживают перезапуск контейнера.
- `dotnet test` зелёный; сборка без предупреждений.

---

## 12. Риски

- **Кука/капча рутрекера** — явный код ошибки, `test-cookie`, баннер; HTML не парсим.
- **Вход по логину/паролю** — если рутрекер включит капчу на `login.php`, автовход остановится
  с `RUTRACKER_LOGIN_CAPTCHA_REQUIRED`; останется ручная кука. Потеря `/data/keys` делает
  сохранённый пароль нечитаемым — его нужно ввести заново. Частые неудачные попытки входа
  могут привести к блокировке аккаунта, поэтому после первой неудачи пауза 10 минут.
- **Неверный infohash** — фикстуры реальных торрентов, парсер как чистая функция с покрытием.
- **qBittorrent недоступен или сменил API** — ошибка пишется в `ApplyError` и не ломает скан;
  замена повторяется на следующем скане или кнопкой. Старая раздача удаляется только после
  успешного поиска, файлы не удаляются, поэтому повтор безопасен.
- **SQLite + bind-mount Docker Desktop** — WAL и `busy_timeout`; при блокировках перейти на named volume.
- **Нет авторизации** — слушать LAN/localhost, предупреждение в README; кука в БД plaintext.
- **Пересечение авто- и ручного скана** — `SemaphoreSlim` + `ScanStatus`.
- **Cloudflare ужесточит проверку** — headless уже не проходит; если перестанет проходить и
  headed-Chromium, останется ручной `cf_clearance`. Обход может нарушать ToS и привести
  к блокировке IP, поэтому интервал по умолчанию 60 минут.

---

## 13. Журнал изменений плана

Формат: дата — что поменяли — почему. Старые формулировки оставляем зачёркнутыми.

- 2026-11-17 — версия 1: первичный план.
- 2026-11-17 — версия 2: `~~история обновлений (TorrentUpdateEvent + /history)~~` → только
  `LastChangedAtUtc` на торренте. `~~таблица StoredTorrentFile, N версий~~` → один файл
  `/data/torrents/{topicId}.torrent`. `~~ScanRun в БД~~` → in-memory `ScanStatus`.
  `~~свой CSS без UI-фреймворка~~` → PrimeVue 4 (MIT). Причина: уточнения заказчика
  (история не нужна, хранить только последний .torrent, UI на бесплатном фреймворке).
- 2026-11-17 — версия 3: `~~TorrentWatcher.sln~~` → `TorrentWatcher.slnx`. Причина: `dotnet new sln`
  в SDK 10.0.401 создаёт slnx по умолчанию; `dotnet build` и `dotnet test` с ним работают.
- 2026-11-17 — TW-02: вместе с парсером заранее созданы `Application/Exceptions/AppException.cs`,
  `Exceptions/ErrorCodes.cs` и обёртка `Application/Parsing/RutrackerLinks.cs`. Причина: DoD
  «невалидный ввод даёт `INVALID_URL`» требует машинного кода уже на уровне use-case; эти файлы
  всё равно нужны в TW-13, просто появились раньше.
- 2026-11-17 — TW-03: реальный rutracker-торрент лежит в тестах с вырезанным passkey
  (`announce`/`announce-list` обнулены на ту же длину, infohash сохранён). Причина: в `dl.php`
  рутрекер выдаёт ссылку с личным `pk=…`, коммитить её нельзя. Остальные фикстуры
  (однофайловый, юникод, битые данные) собираются кодом в тестах.
- 2026-11-17 — TW-04: значения по умолчанию для сида: интервал 60 минут, автоскан включён,
  задержка 1500 мс, таймаут 30 с. Причина: в плане задан только диапазон 1…1440 минут;
  выбран безопасный для трекера интервал, чтобы не долбить его раз в минуту.
- 2026-11-17 — TW-06: добавлен машинный код `RUTRACKER_INVALID_PAYLOAD` и вид сбоя
  `RutrackerFailureKind.InvalidPayload` (200/HTML, bencode без announce, превышение лимита
  размера). Причина: в §5 перечислены только cookie/unavailable/not-found, а «рутрекер вернул
  не .torrent» нужно отличать от сетевого сбоя. `AppException` перестал быть `sealed`, чтобы
  `RutrackerException` наследовал его и middleware (TW-13) обрабатывал оба одинаково.
- 2026-11-17 — TW-07: добавлен машинный код `TORRENT_STORAGE_FAILED` (ошибка записи/удаления
  `.torrent`) и ключ конфигурации `Storage:TorrentsDirectory`. Причина: сбой диска должен
  давать понятный ProblemDetails, а не 500 без кода; путь к файлам нужен разный для контейнера
  (`/data/torrents`, значение по умолчанию) и локальной разработки (`data/torrents`
  в `appsettings.Development.json`). Порт `ITorrentFileStorage` добавлен в Application —
  как и остальные порты из §3, Infrastructure его реализует.
- 2026-11-17 — TW-08: добавлен машинный код `INVALID_SETTINGS`; помимо интервала (1…1440)
  валидируются задержка между запросами (≥ 0) и таймаут (> 0). Причина: нулевой/отрицательный
  таймаут нельзя передать в `HttpClient`, а отрицательная задержка ломает цикл скана.
  Уточнено поведение: `null` в `RutrackerCookie` означает «не менять», а смена куки сбрасывает
  `CookieVerifiedAtUtc`/`CookieLastError` — прежняя проверка к новой куке не относится.
  `SettingsService.Get()` отдаёт куку только маской (последние 4 символа).
- 2026-11-17 — TW-09: добавлен машинный код `TORRENT_NOT_FOUND`. Уточнена семантика первого
  добавления: `LastCheckOutcome = Unchanged`, `LastCheckedAtUtc` = момент скачивания,
  `LastChangedAtUtc = null` (первое появление — не «обновление»). Добавление без куки даёт
  `RUTRACKER_COOKIE_INVALID` до сетевого запроса, а битый bencode от рутрекера —
  `RUTRACKER_INVALID_PAYLOAD` (клиент проверяет только начало и announce). Причина: UI должен
  различать «торрент уже есть», «нет куки» и «сервер вернул мусор».
- 2026-11-17 — TW-10: `TorrentScanService.CheckAsync` возвращает `TorrentCheckResult`
  с `ErrorCode` и `ShouldStopScan` (403/невалидный payload → скан пора прерывать, 404/сеть —
  только этот торрент). Причина: TW-11 нужно отличать отказ куки от локального сбоя топика,
  не разбирая текст `LastError`. Файл .torrent перезаписывается на каждой успешной проверке
  (даже при неизменном infohash), чтобы восстановить вручную удалённый файл; дата обновления
  `LastChangedAtUtc` меняется только при смене infohash.
- 2026-11-17 — TW-11: `ScanCoordinator` сделан singleton'ом с `IServiceScopeFactory` (каждый
  запуск получает свой scope с DbContext), поэтому его можно звать из фонового планировщика
  и из API. Порог остановки — 2 подряд `ShouldStopScan` (план: «2–3»); при остановке
  `SettingsService.InvalidateCookieAsync` сбрасывает `CookieVerifiedAtUtc` и пишет
  `CookieLastError`. `check-all` и автоскан обрабатывают только включённые торренты.
  `ScanStatus` состоит из `RunId/Trigger/StartedAtUtc/FinishedAtUtc/Status/TorrentsTotal/
  ChangedCount/FailedCount/Message`; добавлен `ScanStartResult(Status, Started)` для ответа
  API. Причина: без признака `Started` UI не отличит новый запуск от присоединения к идущему.
  В Application добавлен пакет `Microsoft.Extensions.DependencyInjection.Abstractions`
  (только базовые интерфейсы DI, без ASP.NET и EF Core).
- 2026-11-17 — TW-12: в API добавлен `PUT /api/torrents/{id}/enabled` (пауза/возврат в скан):
  в §5 CRUD перечислен, но эндпоинт паузы не назван, а UI (TW-21) он нужен. Перечисления
  в JSON отдаются строками (`JsonStringEnumConverter`), чтобы TypeScript-клиент не зависел
  от числовых значений. `GET /api/system/status` собирает `SystemStatusService` (версия,
  текущий run, последний/следующий запуск, счётчики, состояние куки); `/api/health`
  остаётся в TW-15.
- 2026-11-17 — TW-13: добавлен код `INTERNAL_ERROR`; статусы: `INVALID_URL`/`INVALID_SETTINGS`
  → 400, `RUTRACKER_COOKIE_INVALID` → 401, `TORRENT_NOT_FOUND`/`TOPIC_NOT_FOUND` → 404,
  `DUPLICATE_TOPIC`/`SCAN_ALREADY_RUNNING` → 409, `RUTRACKER_*_PAYLOAD`/`UNAVAILABLE` → 502,
  `TORRENT_STORAGE_FAILED` → 500. Причина: в §5 коды перечислены, но HTTP-статусы не заданы;
  выбран 401 для отклонённой куки и 502 для сбоя внешнего трекера. Отмена запроса клиентом
  не превращается в 500.
- 2026-11-17 — TW-14: `Scalar.AspNetCore` 3.0.1, документация `/api/docs`, OpenAPI-документ
  `/openapi/v1.json`; маппинг включён во всех окружениях, а не только в Development.
  Причина: приложение self-hosted, Swagger-подобная страница полезна и в контейнере;
  защиты API всё равно нет (см. §2). `/api/docs` без слеша отдаёт редирект на `/api/docs/` —
  это поведение Scalar.
- 2026-11-17 — TW-15: healthcheck проверяет `CanConnectAsync` к SQLite (свой `DatabaseHealthCheck`
  через `IServiceScopeFactory`, без пакета EF-health-checks). Неизвестный `/api/*` отдаёт
  `404` с кодом `ENDPOINT_NOT_FOUND`, `/api/health` — 200/503. `index.html` отдаётся
  с `no-cache`, остальные файлы — `max-age=31536000, immutable`. Удалён шаблонный
  `WeatherForecastController`/`WeatherForecast`, чтобы он не попадал в API и OpenAPI.
- 2026-11-17 — TW-16: `Microsoft.AspNetCore.Mvc.Testing` 10.0.12; `TestWebApplicationFactory`
  подменяет `IRutrackerClient` на `FakeRutrackerClient` и задаёт временные SQLite/каталог
  через `UseSetting`. Причина: тесты не должны ходить на рутрекер и не должны зависеть от
  файлов в рабочем каталоге. Валидация `AddTorrentRequest` перенесена на параметр record'а
  (`[Required]` вместо `[property: Required]`): MVC иначе падает на record-метаданных.
- 2026-11-17 — TW-17/TW-18: планировщик ждёт «последний скан + интервал», после смены настроек
  пересчитывает ожидание (и сверяет снапшот после подписки, чтобы не потерять изменение,
  пришедшее между чтением и подпиской). Если в момент запуска идёт ручной скан, автоскан
  пропускается и ждёт `WaitForIdleAsync`. `HostOptions.ShutdownTimeout = 30 с`. В Application
  добавлен пакет `Microsoft.Extensions.Hosting.Abstractions`; для тестов —
  `Microsoft.Extensions.TimeProvider.Testing` 10.0.0 (`FakeTimeProvider`).
- 2026-11-17 — TW-19: фронтенд собран на Vue 3.5.45, Vite 7.3.8, PrimeVue 4.5.5
  (последняя ветка 4.x: в плане зафиксирован PrimeVue 4, хотя уже есть 5), тема
  `@primeuix/themes` 1.2.5 (Aura), иконки `primeicons` 8.0.2, `vue-router` 4.6.5.
  Vite проксирует `/api` и `/openapi` на `http://localhost:5080`, `npm run build`
  кладёт бандл в `../wwwroot` (шрифты иконок — в `assets`). Проверено: `/` и `/settings`
  отдают `index.html` с `no-cache`, API отвечает 200.
- 2026-11-17 — TW-20: `api/client.ts` — один `fetch`-слой, разбирает `ProblemDetails`
  в `ApiError` (`status`, `problem`, `code`), для 204 возвращает `undefined`. Тосты —
  `useApiError` на `primevue/usetoast`, `<Toast>` в лейауте. `usePolling` не допускает
  пересечения запросов и отдаёт ошибку колбэку; `useSystemStatus` опрашивает статус
  раз в 15 с без тостов (иначе каждый сбой сети давал бы спам). Форматтеры размеров/дат —
  `utils/format.ts`. Pinia не добавляли: состояния мало, composables достаточно.
- 2026-11-17 — TW-21: таблица использует `DataTable`/`Tag`/`ToggleSwitch`, удаление —
  `ConfirmDialog` + `ConfirmationService`, пауза — `PUT /enabled`. Чтобы не плодить запросы,
  строка обновляется из ответа `check`/`enabled`. Поллинг статуса раз в 15 с; когда run
  переходит из Running в Completed/Failed, список перезагружается. Обратный отсчёт —
  локальный таймер 1 с от `nextScanAtUtc`. Бандл после добавления PrimeVue-компонентов
  ~835 КБ (194 КБ gzip); предупреждение Vite о размере чанка не ошибка, код-сплиттинг
  при желании можно добавить позже.
- 2026-11-17 — TW-22: экран настроек не отправляет маску куки обратно; пустое поле означает
  «не менять». Проверка куки использует сохранённое значение (об этом есть подпись в UI),
  после проверки настройки перезагружаются, чтобы показать `cookieVerifiedAtUtc`/ошибку.
  Таймаут запроса вынесен в форму, хотя в §8 он не перечислен: без него PUT не может
  передать валидное значение (сервис требует > 0).
- 2026-11-17 — TW-23: `npm run build` пишет в `wwwroot`; Kestrel на 5099 отдал `/` и
  `/settings` как SPA с `no-cache`, API — 200, в браузере нет ошибок консоли.
  ~~В `.gitignore` уже были `node_modules/` и `ClientApp/dist/`; сгенерированный `wwwroot`
  не игнорируется — в Docker он собирается заново, локально нужен для `dotnet run`.~~
  `wwwroot` в git не хранится (см. запись ниже).
- 2026-11-17 — TW-24–TW-26: образ собирается в три стадии (`node:24-alpine` → `sdk:10.0` →
  `aspnet:10.0-noble`), публикуется только `TorrentWatcher.Web.csproj`; тесты в контекст
  не копируются (`.dockerignore`), поэтому `restore` идёт по Web-проекту, а не по `slnx`.
  В официальном aspnet-образе уже есть пользователь `app` (UID 1654), поэтому `groupadd`
  не нужен; `curl` ставится только для HEALTHCHECK. Compose: `./data:/data`,
  `TZ=Europe/Moscow`, плюс те же env-ключи, что в Dockerfile.
  Smoke-тест: контейнер поднялся healthy; в контейнере временно запущен локальный HTTPS-сервер
  с тестовым `.torrent`, `rutracker.org` через `/etc/hosts` указывал на него, самоподписанный
  сертификат добавлен в CA store. Через API добавлен торрент (`HTTP 201`), появился файл
  `/data/torrents/654321.torrent`; после `docker compose restart` список из двух торрентов,
  настройки (кука-маска, интервал 15 мин) и файлы на месте. Изменений в коде для подмены
  рутрекера не потребовалось; тестовые `data/` и контейнер удалены после проверки.
- 2026-11-17 — TW-27: README описывает Docker/локальный запуск, env-ключи, архитектуру,
  жизненный цикл скана, API с кодами ошибок, БД/миграции и ограничения. `.http`-коллекция
  переписана с шаблонного weatherforecast на реальные эндпоинты (добавление, пауза, проверки,
  настройки, health, OpenAPI).
- 2026-11-17 — TW-28: добавлены порт `ITorrentClient` и `TorrentClientAddRequest` без
  реализации; заметка `docs/qbittorrent.md` описывает миграцию настроек и колонок
  `AppliedInfoHashV1`/`AppliedAtUtc`, идемпотентное добавление, обработку сетевых ошибок
  клиента и UI. Причина: задел должен быть в коде, а не только в тексте плана.
- 2026-11-17 — после TW-28: собранный фронтенд больше не версионируется —
  `src/TorrentWatcher.Web/wwwroot/` добавлен в `.gitignore` и удалён из индекса.
  Было: бандл из `npm run build` коммитился вместе с исходниками. Стало: `wwwroot`
  пересобирается командой `npm ci && npm run build` (локально) или стадией
  `node:24-alpine` в Dockerfile (в образе). Причина: бинарные хэшированные ассеты
  не должны попадать в историю — они дают шумные диффы и конфликты при каждой сборке;
  Docker-сборка и без них самодостаточна.
- 2026-11-20 — версия 4: добавлен этап 10 (TW-29–TW-33) — обход Cloudflare на `dl.php`.
  Причина: рутрекер включил managed challenge (`403`, `Cf-Mitigated: challenge`) на скачивание
  вложений, прежний `HttpClient` с кукой рутрекера получает страницу проверки.
  Проверено экспериментально: headless-Chromium (в том числе с `--disable-blink-features=
  AutomationControlled` и без `--enable-automation`) челлендж не проходит; headed-Chromium
  проходит; выданный `cf_clearance` работает с обычным `curl`/`HttpClient` при совпадении
  IP и User-Agent. Поэтому браузер используется только как «солвер», а загрузка остаётся
  на `HttpClient`. Срок жизни `cf_clearance` в проверке — около года.
  ~~Капча не обходится, HTML не парсим~~ → капча Cloudflare обходится браузером; HTML
  по-прежнему не парсим.
- 2026-11-20 — TW-29: 403 от Cloudflare больше не считается невалидной кукой. Добавлены
  `RutrackerFailureKind.Challenge`, код `RUTRACKER_CHALLENGE_REQUIRED` (HTTP 502) и
  распознавание челленджа по заголовку и по HTML-маркерам (Cloudflare умеет отдавать
  интерстишл с кодом 200). Причина: иначе UI требовал менять исправную куку.
- 2026-11-20 — TW-30/TW-31: добавлены порты `ICloudflareChallengeSolver`,
  `ICloudflareClearanceProvider`, `IRutrackerHttpClient`; `RutrackerClient` стал чистым
  транспортом, обёртка `CloudflareAwareRutrackerClient` подставляет пропуск и один раз
  повторяет запрос после решения. Пропуск и UA хранятся в `AppSetting` (миграция
  `AddCloudflareClearance`), в журнал пишется только срок действия. Причина: держать
  Playwright вне загрузки `.torrent` — дешевле по памяти и проще тестировать.
- 2026-11-20 — TW-32: в настройках появились ручные `cf_clearance` и User-Agent (аварийный
  режим и случай, когда браузер в контейнере недоступен), кнопка решения челленджа и статус.
  Смена UA без нового пропуска сбрасывает старый: Cloudflare привязывает clearance к UA.
- 2026-11-20 — TW-33: образ ставит Chromium через `dotnet Microsoft.Playwright.dll install
  --with-deps chromium` и запускается под `xvfb-run`; профиль браузера — `/data/browser`,
  в compose добавлен `shm_size: 1gb`. Причина: headless не проходит проверку, а `--with-deps`
  сам ставит системные библиотеки. Docker-сборка в этом окружении не проверялась
  (демон Docker не запущен) — проверить при первом `docker compose up --build`.
- 2026-11-21 — проверка Docker доведена до конца. Сборка и запуск прошли, но всплыли
  четыре проблемы, все исправлены:
  1. `dotnet Microsoft.Playwright.dll install` не запускается без
     `Microsoft.Playwright.runtimeconfig.json` рядом с DLL — файл создаётся на время установки
     и удаляется.
  2. Встроенному Node из Playwright не хватает `libatomic.so.1` — в apt-слой добавлен
     `libatomic1`, иначе `--with-deps` не стартует (курица и яйцо).
  3. `xvfb-run` как PID 1 зависает на ожидании SIGUSR1 от Xvfb, а при перезапуске контейнера
     остаются `/tmp/.X99-lock` и сокет. Вместо `xvfb-run` — свой
     `docker-entrypoint.sh`: чистит lock/сокет, поднимает `Xvfb :99` и делает `exec dotnet`.
  4. **Главное:** предположение «clearance работает с обычным `HttpClient`» в Linux-контейнере
     не подтвердилось. С валидным `cf_clearance` и тем же UA `index.php` отдаёт 200, а
     `dl.php`/`viewforum.php` — `403 Cf-Mitigated: challenge` и для `HttpClient`, и для `curl`;
     headed-Chromium с теми же куками скачивает `.torrent` (проверено на реальных топиках).
     Значит, Cloudflare проверяет ещё и браузерный отпечаток, а не только IP+UA.
     ~~Браузер используется только как «солвер», загрузка всегда на `HttpClient`~~ →
     добавлен порт `IBrowserTorrentDownloader` и fallback: `HttpClient` → решение челленджа →
     повтор → скачивание Chromium. Флаг `CloudflareTransportState` (singleton) после первого
     успешного браузерного скачивания переключает процесс на браузер, чтобы не тратить 403
     на каждый скан. Кука рутрекера кладётся в контекст Chromium (иначе `dl.php` отвечает
     `attachment data not found`); профиль и так лежит в `/data`.
  Проверено в контейнере: образ 1.57 ГБ, healthcheck healthy, добавление реального топика
  `6442751` (infohash `a024b8…`, 1.5 ГБ, 1 файл), первый `check` — 3.0 с, повторный — 0.26 с,
  `test-cookie` — 0.29 с; после `docker compose restart` данные, профиль и сканирование
  сохранились. Добавлен `start.bat` (проверка Docker, сборка, ожидание health, открытие UI).
- 2026-11-21 — версия 6: ~~ручная кука рутрекера в настройках~~ → логин и пароль.
  Причина: браузер всё равно поднимается для Cloudflare, поэтому вход по учётным данным
  не добавляет отдельного механизма, а копировать куку вручную больше не нужно.
  Пароль шифруется ASP.NET Core Data Protection, ключи — `/data/keys` (вне БД);
  `RutrackerPasswordProtected` в API и журнал не попадает. Кэш куки сеанса по-прежнему
  хранится в `AppSetting.RutrackerCookie` (plaintext, как и раньше): после входа им
  пользуется обычный `HttpClient`, браузер на каждый запрос не запускается. Смена логина
  или пароля сбрасывает кэш и подтверждение сеанса. После неудачного входа 10 минут
  автоматические попытки не повторяются, чтобы не блокировать аккаунт; кнопка «Войти»
  паузу игнорирует. Перед входом из профиля Chromium удаляются только куки `bb_*`
  (чтобы смена аккаунта не вернула старый сеанс), `cf_clearance` и `__cf_bm` сохраняются.
  Ручная кука оставлена в «Дополнительно» как аварийный путь.
  Добавлены коды `RUTRACKER_LOGIN_FAILED` (401), `RUTRACKER_LOGIN_CAPTCHA_REQUIRED` (409),
  `RUTRACKER_CREDENTIALS_UNREADABLE` (409).
  Проверено: `dotnet test` — 198 unit + 28 integration; локальный запуск — миграция
  `AddRutrackerCredentials`, создание ключей, сохранение логина/пароля через API без выдачи
  пароля, UI настроек без ошибок консоли. Форма входа проверена в реальном Chromium:
  `login.php` содержит `login-form-full` с `login_username`/`login_password`, капчи
  в форме сейчас нет. Успешный вход с настоящими учётными данными не выполнялся
  (их нет в окружении) — проверить при первом реальном использовании; ошибки входа
  и капча обрабатываются отдельными кодами.
- 2026-11-21 — TW-36 (исправление): вход падал с `System.TimeoutException` из
  `TryReadLoginErrorAsync` (HTTP 500 `INTERNAL_ERROR`), хотя страница входа уже ушла на index.
  Причина: с Playwright 1.44 таймаут бросает отдельный `TimeoutException`, а не
  `PlaywrightException`; проба `#login-form-full` в цикле шла с дефолтным таймаутом 30 с
  и блокировала опрос, поэтому `bb_session`, появившаяся на следующей странице, не читалась
  (в профиле Chromium после сбоя кука осталась). Стало: все пробы состояния страницы ждут
  не больше 2 с (`ProbeTimeoutMs`) и ловят оба вида исключений, текст ошибки формы читается
  только когда форма видна, для контекста задан общий таймаут 15 с, а закрытие страницы
  не маскирует результат операции.
  Попутно нашлись ещё три причины того, что вход «не завершался»:
  `~~context.ClearCookiesAsync(new BrowserContextClearCookiesOptions { Name = "^bb_" })~~`
  → `NameRegex = new Regex("^bb_")`: `Name` сравнивается с именем куки точно, поэтому
  старый сеанс не удалялся и `login.php` уводил на index; `~~CookiesAsync([BaseUrl, ForumUrl])~~`
  → адреса с завершающим слешем (`CookieUrls`): `bb_*` ставятся с путём `/forum/`, и без
  слеша фильтр Playwright их не видел; бюджет входа увеличен до
  `SolveTimeout + 30 с`, потому что Cloudflare может задержать и навигацию, и POST.
  Добавлено: если формы так и нет, но `bb_*` уже есть, вход считается выполненным;
  при неудаче в журнал пишется состояние страницы (url, title, челлендж, куки по именам,
  разметка кнопки). Ещё одна причина того же сообщения — брошенный `SingletonLock`
  в профиле Chromium после перезапуска контейнера: Chromium пишет «profile appears to be
  in use», не может показать диалог и падает с кодом 21. Теперь `LaunchContextAsync`
  при таком отказе снимает `SingletonLock`/`SingletonCookie`/`SingletonSocket`/`lockfile`
  и повторяет запуск один раз.
  Проверено: `dotnet test` (198 + 29), реальный вход `eric-1` через
  API занял 2 с, затем `test-cookie` и `check-all` (топик 6442751) отработали без ошибок.
- 2026-11-21 — TW-35 (исправление): `test-cookie` падал с
  «another instance with the same key value for {'Id'} is already being tracked», если
  в одном запросе настройки читались дважды (например, сохранение пропуска Cloudflare
  после решения челленджа). Причина: `AppSettingsRepository.FindAsync` работает
  `AsNoTracking`, а `SaveAsync` делал `Update` для второго отсоединённого экземпляра,
  пока первый ещё отслеживался. Стало: `SaveAsync` отсоединяет прежний экземпляр с тем же
  ключом и только потом присоединяет новый. Тест —
  `SqlitePersistenceTests.Settings_TwoSavesInOneContext_DoNotConflict`.
- 2026-11-21 — исправление гонки записи настроек: `POST /api/torrents` иногда отвечал 401
  сразу после `PUT /api/settings` с кукой. Причина: строку настроек меняют несколько
  писателей (экран настроек, планировщик, сеанс рутрекера, пропуск Cloudflare), и все
  делают read-modify-write; скан, завершившийся между запросом и сохранением куки,
  записывал устаревший снимок и стирал её. Стало: singleton `SettingsWriteGate`
  (`SemaphoreSlim`) в Application; `SettingsService`, `CloudflareClearanceService`
  и `RutrackerSessionService` берут его вокруг чтения и записи настроек, а сетевые
  вызовы и вход в браузер остаются вне блокировки. Интеграционные тесты перестали
  падать с 401 (5 прогонов подряд). Причина именно гонки, а не нового кода, подтверждена
  нестабильностью до правки: один и тот же тест проходил и падал от прогона к прогону.
- 2026-11-21 — Dockerfile: `COPY` опубликованного приложения перенесён после установки
  Chromium, а в слой установки копируются только `Microsoft.Playwright.dll` и каталог
  `.playwright` с драйвером (CLI без него пишет «missing required assets»). Причина:
  раньше любое изменение кода сбрасывало слой с `playwright install --with-deps chromium`
  и каждая пересборка заново скачивала ~325 МБ браузеров; теперь браузеры ставятся
  один раз и переустанавливаются только при смене версии Playwright.
- 2026-11-21 — версия 7: добавлен этап 12 (TW-39) — интеграция с qBittorrent.
  Причина: заказчик попросил автоматически обновлять раздачи в qBittorrent, запущенном
  на том же сервере. Решение: API v2 Web UI; при смене infohash старая раздача удаляется
  с `deleteFiles=false`, новая добавляется в ту же папку с категорией и тегами, затем
  recheck и resume. Пароль шифруется Data Protection; авто-применение можно выключить,
  тогда работает кнопка «Применить». Сбой клиента не делает проверку ошибочной.
  Добавлены коды `QBITTORRENT_NOT_CONFIGURED` (409), `QBITTORRENT_AUTH_FAILED` (409),
  `QBITTORRENT_UNAVAILABLE` (502), поля `AppliedInfoHashV1`/`AppliedAtUtc`/
  `SupersededInfoHashV1`/`ApplyError` и миграция `AddQbittorrentIntegration`.
  Проверено: `dotnet test` — 222 unit + 31 integration, `npm run build` зелёный.
  Реальное подключение к qBittorrent из этого окружения не выполнялось (нет доступа
  к `plex.local`) — проверить при первом запуске; в README описаны сеть контейнера,
  `Host header validation` и конфликт порта 8080.
- 2026-11-21 — версия 8: добавлен этап 13 (TW-40) — публикация на сервере и смена порта.
  Причина: на сервере порт 8080 занят qBittorrent; приложение теперь публикуется на 8088
  (`APP_PORT`, внутри контейнера по-прежнему 8080). Добавлен `deploy_server.bat` по образцу
  `C:\Work\ProxyTunnel\deploy_server.bat`: сборка образа, `docker save`, копирование tar,
  compose и `remote-deploy.sh` через `pscp`, развёртывание через `plink`. На сервере
  compose создаётся один раз в `/torrentwatcher`, права на `data` выставляются пользователю
  `app`, открывается порт в ufw/firewalld, проверяется healthcheck, хранятся теги для
  rollback. Проверено локально: сборка, `dotnet test` (222 + 31), `npm run build`;
  реальный деплой на сервер из этого окружения не выполнялся — проверить при первом запуске.
- 2026-11-21 — TW-40 (уточнение): деплой под непривилегированной УЗ. Причина: на сервере
  нет пароля root, публиковать нужно под учёткой из sudoers. `deploy_server.bat` теперь
  спрашивает SSH-пользователя (по умолчанию root) и пароль sudo (пусто — NOPASSWD);
  пароль передаётся на сервер через stdin, в файл и журнал не пишется. `remote-deploy.sh`
  принимает режим `nopass`/`stdin`, использует `sudo -n`/`sudo -S`, а при отсутствии sudo
  может работать от группы `docker` с `INSTALL_DIR` в домашнем каталоге; каталоги
  установки и `state` передаются деплоящему пользователю, `data` — пользователю `app`.
  Проверено: `bash -n`, полный прогон `deploy_server.bat` с заглушками для режимов
  root / nopass / пароль sudo.
- 2026-11-21 — TW-40 (уточнение 2): ускорение передачи образа. Причина: 1,74 ГБ tar
  по SSH в локальной сети шёл со скоростью ~2–2,5 МБ/с (~12 минут). `pscp` теперь
  запускается с `-C` (zlib-сжатие потока; tar с Chromium и .NET ужимается примерно
  в 2–2,5 раза), отключается `PSCP_COMPRESS=0`; в README добавлена диагностика
  (линк, Wi-Fi, CPU) и упоминание возможной сборки на сервере. Сжатие tar в отдельный
  `.tar.gz` пробовал, но `docker load` не читает gzip, а `tar -czf` оборачивает tar
  во внешний архив — поэтому выбран потоковый `-C`.
- 2026-11-21 — TW-40 (исправление): первый реальный деплой под `eric` упал на двух местах.
  1) `chown` в контейнере выполнялся от `USER app` и получал `Operation not permitted` —
     добавлен `--user root` для одноразового контейнера, меняющего владельца `data`.
  2) `docker compose -f` на сервере дал `unknown shorthand flag: 'f' in -f`: плагин
     compose не подхватился. Скрипт теперь проверяет `docker compose version`, затем
     `docker-compose`, и только потом падает с подсказкой об установке; порт передаётся
     через `.env` рядом с compose, чтобы не зависеть от `env`/sudoers.
  Заодно подтвердилось ускорение передачи: с `pscp -C` скорость выросла с ~2,5 до
  ~10,9 МБ/с на том же канале.
- 2026-11-21 — TW-40 (исправление 2): пароль sudo и код возврата.
  1) SSH-сервер печатает «Access granted. Press Return to begin session.» и съедал
     первую строку stdin, поэтому переданный через поток пароль не доходил до скрипта.
     Теперь `deploy_server.bat` кладёт пароль в локальный временный файл, отправляет
     его `pscp` рядом с остальными файлами, а `remote-deploy.sh` читает его, сразу
     ставит права `600` и удаляет; stdin больше не используется.
  2) `goto` сбрасывал ERRORLEVEL, и при провале удалённого скрипта батник печатал
     «Готово». Код возврата `plink` теперь записывается сразу после вызова: ошибка
     деплоя завершает батник с кодом 1 и не показывает успех.
  Проверено с заглушками: успешный прогон (пароль передаётся файлом) и провал
  удалённой команды (код 7) — батник пишет ошибку, локальный файл пароля удаляется.
