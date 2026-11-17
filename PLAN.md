# TorrentWatcher — план реализации

Статус: в работе (TW-01 готов).
Следующий пункт: **TW-02**.
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

- [ ] TW-02. `RutrackerUrlParser`: `viewtopic.php?t=`, `#t=`, `dl.php?t=`, голый числовой id.
      Тесты на все формы + мусор. Готово: невалидный ввод даёт `INVALID_URL`.
- [ ] TW-03. `BencodeReader` + `TorrentMetadataParser`: infohash v1, `name`, размер, число файлов,
      `private`. Тесты на фикстурах (реальный rutracker-торрент, однофайловый, юникод, битые данные).

### Этап 2. Данные (3 ч)

- [ ] TW-04. `AppDbContext`, конфигурации, индексы, миграция `Initial`, сид строки настроек.
      Готово: миграция накатывается на пустую БД.
- [ ] TW-05. Репозитории портов Application, WAL/`busy_timeout`/`foreign_keys` при старте.
      Готово: перезапуск сохраняет данные.

### Этап 3. Внешний мир (4 ч)

- [ ] TW-06. `RutrackerClient` + `RutrackerException` + тесты на фейковом `HttpMessageHandler`
      (200 бенкод, 200 HTML, 403, таймаут, слишком большой ответ).
- [ ] TW-07. `TorrentFileStorage`: одна версия файла, атомарная запись tmp→rename, удаление.
      Готово: после перезаписи на диске ровно один `.torrent`.

### Этап 4. Приложение (8 ч)

- [ ] TW-08. `SettingsProvider` + `SettingsService`: чтение/запись, валидация, событие `Changed`,
      `TestCookieAsync`. Готово: интервал вне 1…1440 отклоняется.
- [ ] TW-09. `TorrentService`: add (URL → скачивание → сохранение → дубликаты), list, delete,
      включение/пауза.
- [ ] TW-10. `TorrentScanService`: сравнение infohash, `Changed/Unchanged/Error`, обновление
      `LastChangedAtUtc`, `LastError`, `LastCheckOutcome`. Юнит-тесты на все ветки.
- [ ] TW-11. `ScanCoordinator` + `ScanStatus`: single-flight, ручной и авто-триггер, счётчики,
      прерывание по невалидной куке. Готово: второй параллельный запуск не стартует.

### Этап 5. Web API (6 ч)

- [ ] TW-12. Контроллеры `Torrents/Settings/System`, DTO, валидация входа.
- [ ] TW-13. Middleware исключений → `ProblemDetails` с кодами ошибок; без стектрейсов в ответе.
- [ ] TW-14. OpenAPI (`Microsoft.AspNetCore.OpenApi`) + Scalar на `/api/docs`.
- [ ] TW-15. Статика, SPA fallback, `/api/health`, `/api/system/status`, кэш-заголовки.
- [ ] TW-16. Интеграционные тесты (`WebApplicationFactory` + временная SQLite): CRUD, 409,
      неизвестный `/api/...` отдаёт 404, а не `index.html`.

### Этап 6. Планировщик (4 ч)

- [ ] TW-17. `ScanScheduler`: цикл, реакция на смену настроек, выключение автоскана, graceful stop,
      логирование старта/финиша run.
- [ ] TW-18. Тесты планировщика с `TimeProvider`. Готово: смена интервала в UI меняет время
      следующего запуска без рестарта.

### Этап 7. Frontend (10 ч)

- [ ] TW-19. Скаффолд Vite + Vue 3 + TS, PrimeVue/Aura/PrimeIcons, роутер, лейаут, тема.
- [ ] TW-20. Типизированный API-клиент, обработка `ProblemDetails`, тосты, composables.
- [ ] TW-21. Экран «Торренты»: таблица, добавление, удаление с подтверждением, форс-проверка
      одного/всех, бейджи статусов, поллинг, обратный отсчёт.
- [ ] TW-22. Экран «Настройки»: кука, интервал, автоскан, задержка, сохранение без рестарта,
      кнопка проверки куки.
- [ ] TW-23. `npm run build` → `wwwroot`, прод-проверка через Kestrel (SPA + API на одном порту).

### Этап 8. Docker (4 ч)

- [ ] TW-24. `Dockerfile` multi-stage, `.dockerignore`, non-root, healthcheck.
- [ ] TW-25. `docker-compose.yml`, volume, env, базовая инструкция в README.
- [ ] TW-26. Smoke-тест: поднять, добавить торрент, перезапустить контейнер, данные на месте.

### Этап 9. Полировка (3 ч)

- [ ] TW-27. README: архитектура, API, эксплуатация, ограничения (нет авторизации), `.http`-коллекция.
- [ ] TW-28. Заметка «как добавить qBittorrent позже»: точка расширения `ITorrentClient`,
      какие данные уже есть.

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
