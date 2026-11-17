# TorrentWatcher — план реализации

Статус: план выполнен (TW-01–TW-28).
Следующий пункт: **нет — все задачи закрыты**.
Версия плана: 3 (2026-11-17).

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
- Web UI на Vue 3 + TS, API на ASP.NET Core (net10), SQLite + EF Core, Docker.

### Не входит в скоуп (сейчас)

- Интеграция с qBittorrent (только задел в виде порта и статуса «обнаружено обновление»).
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
| Кука рутрекера | одна глобальная, в таблице настроек, plaintext |
| Период | глобальный, минуты, 1…1440, применяется на лету |
| Заголовок торрента | `name` из info-словаря `.torrent`; HTML рутрекера не парсим |
| Хранение `.torrent` | только последний файл: `/data/torrents/{topicId}.torrent` |
| История | не храним; «дата последнего обновления» = `TrackedTorrent.LastChangedAtUtc` |
| Статус скана | in-memory singleton (без таблицы) |
| Планировщик | один `BackgroundService` с `while(true)` + `Task.Delay`, без Quartz/Hangfire |
| UI-фреймворк | PrimeVue 4 (MIT) + тема Aura, иконки PrimeIcons |
| Обнаружено обновление | статус `UpdateAvailable` + дата; ничего не качаем |
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
- `Application`: `Abstractions/*` (порты), `Services/{TorrentService,TorrentScanService,SettingsService,ScanCoordinator}.cs`,
  `State/ScanStatus.cs`, `Dtos/*`, `Exceptions/AppException.cs`
- `Infrastructure`: `Persistence/AppDbContext.cs`, `Persistence/Configurations/*`, `Persistence/Migrations/*`,
  `Rutracker/RutrackerClient.cs`, `Storage/TorrentFileStorage.cs`
- `Web`: `Program.cs`, `Controllers/{Torrents,Settings,System}Controller.cs`, `Contracts/*`,
  `Middleware/ExceptionHandlingMiddleware.cs`, `wwwroot/*`, `ClientApp/*`

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
| RutrackerCookie | `bb_session=...; bb_data=...` |
| CookieVerifiedAtUtc / CookieLastError | результат последней проверки |
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
| POST | `/api/settings/test-cookie` | пробное скачивание по первому отслеживаемому топику |
| GET | `/api/system/status` | версия, текущий run, последний/следующий запуск, счётчики, состояние куки |
| GET | `/api/health` | healthcheck для Docker |

Ошибки — RFC 7807 `ProblemDetails`, коды: `RUTRACKER_COOKIE_INVALID`, `RUTRACKER_UNAVAILABLE`,
`TOPIC_NOT_FOUND`, `INVALID_URL`, `DUPLICATE_TOPIC`, `SCAN_ALREADY_RUNNING`.

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

Задел под qBittorrent: порт `ITorrentClient` без реализации; статус `Changed` в UI трактуется как
«обновление обнаружено, применение не автоматизировано».

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
- Экран «Настройки»: кука, интервал, автоскан, задержка, «Сохранить» (сразу) и «Проверить куку».
- `npm run build` → `../wwwroot`. Dev: Vite проксирует `/api` на `http://localhost:5080`.
- Kestrel: `UseDefaultFiles` + `UseStaticFiles` (index.html — no-cache, ассеты — immutable),
  `MapControllers`, затем `/api/{**rest}` → 404, затем `MapFallbackToFile("index.html")`
  без `:nonfile` (иначе ломаются маршруты Vue).

---

## 9. Контейнер

Multi-stage: `node:24-alpine` (npm ci + build) → `sdk:10.0` (publish с готовым `wwwroot`) →
`aspnet:10.0-noble`. Non-root, `EXPOSE 8080`, `VOLUME /data`, `HEALTHCHECK` на `/api/health`.

Compose: `8080:8080`, `./data:/data`, `ConnectionStrings__Default=Data Source=/data/torrentwatcher.db`,
`TZ=Europe/Moscow`, `restart: unless-stopped`.

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

**Итого: ~48 ч.**

---

## 11. Definition of Done (проекта)

- `docker compose up -d` на чистой машине поднимает приложение, UI доступен на `/`, API на `/api`.
- Кука сохраняется через UI и проверяется кнопкой; невалидная кука даёт понятный баннер.
- Торрент добавляется по ссылке на топик и сразу показывает название, размер и infohash.
- Автоскан идёт с заданным интервалом; смена интервала и куки применяется без перезапуска.
- Форс-проверка одного и всех работает; при изменении infohash обновляется дата и статус.
- Данные и `.torrent` переживают перезапуск контейнера.
- `dotnet test` зелёный; сборка без предупреждений.

---

## 12. Риски

- **Кука/капча рутрекера** — явный код ошибки, `test-cookie`, баннер; HTML не парсим.
- **Неверный infohash** — фикстуры реальных торрентов, парсер как чистая функция с покрытием.
- **SQLite + bind-mount Docker Desktop** — WAL и `busy_timeout`; при блокировках перейти на named volume.
- **Нет авторизации** — слушать LAN/localhost, предупреждение в README; кука в БД plaintext.
- **Пересечение авто- и ручного скана** — `SemaphoreSlim` + `ScanStatus`.

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
