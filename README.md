# TorrentWatcher

Self-hosted приложение для отслеживания топиков рутрекера. Оно периодически скачивает
актуальный `.torrent` по неизменной ссылке `dl.php?t={id}` и сравнивает infohash: если
раздача обновилась, в UI появляется дата изменения. Приложение не качает содержимое
раздачи и не управляет торрент-клиентом.

> Авторизации нет. Слушайте приложение только на localhost или в доверенной LAN.
> Кука рутрекера хранится в SQLite в открытом виде — как и весь файл `/data`.

## Возможности

- Список отслеживаемых топиков: добавление по ссылке, пауза, удаление.
- Глобальная кука рутрекера с маскированием в API и пробной проверкой.
- Автоскан с глобальным интервалом 1…1440 минут; интервал, кука и автоскан применяются
  без перезапуска.
- Ручная проверка одного торрента (синхронно) и всех включённых (в фоне со статусом).
- Определение обновления по infohash v1, хранение только последнего `.torrent`.
- Веб-интерфейс на Vue 3 + PrimeVue и API на ASP.NET Core.

## Быстрый старт (Docker)

```bash
docker compose up -d --build
```

- UI: <http://localhost:8080>
- API: <http://localhost:8080/api>
- OpenAPI/Scalar: <http://localhost:8080/api/docs>

1. Откройте «Настройки», вставьте куку рутрекера (`bb_session=…; bb_data=…`) и сохраните.
2. Нажмите «Проверить куку» — проверка скачивает `.torrent` первого отслеживаемого топика;
   для первой проверки добавьте хотя бы один торрент.
3. На экране «Торренты» вставьте ссылку на топик `https://rutracker.org/forum/viewtopic.php?t=…`.

Данные лежат в `./data`:

```
data/
├─ torrentwatcher.db        # SQLite (WAL)
└─ torrents/{topicId}.torrent
```

`docker compose down` сохраняет данные; `docker compose down -v` их не удалит, потому что
используется bind-mount `./data` — удалите каталог вручную, если нужен чистый старт.

## Переменные окружения

| Переменная | По умолчанию | Назначение |
|---|---|---|
| `ConnectionStrings__Default` | `Data Source=/data/torrentwatcher.db` | Строка подключения SQLite |
| `Storage__TorrentsDirectory` | `/data/torrents` | Каталог последних `.torrent` |
| `ASPNETCORE_URLS` | `http://+:8080` | Адрес Kestrel |
| `ASPNETCORE_ENVIRONMENT` | `Production` | Окружение ASP.NET Core |
| `TZ` | `Europe/Moscow` (compose) | Часовой пояс контейнера |

В локальной разработке `appsettings.Development.json` задаёт `data/torrents`, чтобы файлы
не попадали в системный `/data`.

## Локальная разработка

Требования: .NET SDK 10.0.401+, Node.js 24+.

```bash
dotnet build
dotnet test
dotnet run --project src/TorrentWatcher.Web   # http://localhost:5080
```

Фронтенд отдельно (Vite проксирует `/api` и `/openapi` на 5080):

```bash
cd src/TorrentWatcher.Web/ClientApp
npm ci
npm run dev
npm run build                                  # → ../wwwroot
```

Прод-режим локально: `npm run build`, затем `dotnet run` — Kestrel отдаёт SPA и API с одного
порта. `wwwroot/index.html` кешируется с `no-cache`, файлы в `assets/` — `immutable`.

Собранный фронтенд в репозитории не хранится (`src/TorrentWatcher.Web/wwwroot/` в `.gitignore`):
после клонирования его нужно собрать один раз командой `npm ci && npm run build` из
`ClientApp`. В Docker-образе сборка выполняется автоматически в стадии `node:24-alpine`.

## Архитектура

Три слоя, зависимости строго в одну сторону:

```
Domain ← Application ← Infrastructure
                     ← Web (Infrastructure — только для DI)
```

- `TorrentWatcher.Domain` — сущности, парсер ссылок, bencode и метаданных `.torrent`.
- `TorrentWatcher.Application` — use-cases, порты, DTO, планировщик и координатор сканов.
- `TorrentWatcher.Infrastructure` — EF Core + SQLite, HTTP-клиент рутрекера, файлы.
- `TorrentWatcher.Web` — MVC-контроллеры, ProblemDetails, статика, `Program.cs`.
- `TorrentWatcher.Web/ClientApp` — Vue 3 + TypeScript + Vite + PrimeVue 4.

Подробный план и история решений — в `PLAN.md`.

### Как идёт скан

1. `ScanScheduler` ждёт `LastScanAtUtc + интервал`; изменение настроек пересчитывает ожидание.
2. `ScanCoordinator` не даёт авто- и ручному скану пересечься. Второй `check-all` возвращает
   уже идущий run (`202 Accepted` + `runId`).
3. Для каждого включённого торрента `TorrentScanService` скачивает `.torrent`, считает
   infohash и сохраняет файл атомарно через временный файл + rename.
4. Совпадение хеша → `Unchanged`, различие → `Changed` и `LastChangedAtUtc = now`.
5. Две подряд ошибки «403/не .torrent» прерывают run, кука помечается невалидной.
6. Между запросами выдерживается `RequestDelayMs`; статус доступен в UI и `/api/system/status`.

## API

Полная схема — `/api/docs`; OpenAPI JSON — `/openapi/v1.json`.

| Метод | Путь | Назначение |
|---|---|---|
| GET | `/api/torrents` | Список и сводка |
| POST | `/api/torrents` | Добавить по `{ "url": "…" }` |
| DELETE | `/api/torrents/{id}` | Убрать и удалить `.torrent` |
| PUT | `/api/torrents/{id}/enabled` | Пауза/возврат в автоскан |
| POST | `/api/torrents/{id}/check` | Проверить один торрент синхронно |
| POST | `/api/torrents/check-all` | Запустить проверку всех в фоне |
| GET/PUT | `/api/settings` | Настройки; PUT применяется сразу |
| POST | `/api/settings/test-cookie` | Пробное скачивание |
| GET | `/api/system/status` | Версия, run, расписание, счётчики, кука |
| GET | `/api/health` | Healthcheck (SQLite) |
| GET | `/api/docs` | Scalar UI |

Ошибки — RFC 7807 `ProblemDetails` с полями `code` и `traceId`, без стектрейсов.
Коды: `INVALID_URL`, `INVALID_SETTINGS`, `DUPLICATE_TOPIC`, `TORRENT_NOT_FOUND`,
`TOPIC_NOT_FOUND`, `RUTRACKER_COOKIE_INVALID`, `RUTRACKER_UNAVAILABLE`,
`RUTRACKER_INVALID_PAYLOAD`, `TORRENT_STORAGE_FAILED`, `SCAN_ALREADY_RUNNING`,
`ENDPOINT_NOT_FOUND`, `INTERNAL_ERROR`.

Примеры запросов — `src/TorrentWatcher.Web/TorrentWatcher.Web.http`.

## База данных и миграции

SQLite включается с WAL, `busy_timeout=5000` и `foreign_keys=on`. Миграции накатываются
при старте приложения.

```bash
dotnet ef migrations add <Name> -p src/TorrentWatcher.Infrastructure -s src/TorrentWatcher.Web
dotnet ef database update -p src/TorrentWatcher.Infrastructure -s src/TorrentWatcher.Web
```

## Ограничения

- Нет авторизации и мультипользовательности; одна глобальная кука.
- Кука в БД plaintext, файл БД и `.torrent` доступны владельцу хоста.
- Нет уведомлений (Telegram/ntfy/e-mail) и истории обновлений — только
  `LastChangedAtUtc` и последний статус.
- Интеграция с qBittorrent не реализована: есть только точка расширения `ITorrentClient`;
  пошаговый план — `docs/qbittorrent.md`.
- Хранится только последняя версия `.torrent`, не архив.
- Хост рутрекера фиксирован (`rutracker.org`), прокси не поддерживается.
- Капча/смена вёрстки рутрекера не обходятся: HTML не парсим, при отказе куки показываем
  ошибку и баннер.

## Диагностика

```bash
docker compose ps
docker compose logs -f torrentwatcher
curl -i http://localhost:8080/api/health
```

Если контейнер не стартует на Windows с bind-mount, проверьте права на `./data`; при
блокировках SQLite можно перейти на named volume (см. риск в `PLAN.md`).
