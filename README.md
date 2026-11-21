# TorrentWatcher

Self-hosted приложение для отслеживания топиков рутрекера. Оно периодически скачивает
актуальный `.torrent` по неизменной ссылке `dl.php?t={id}` и сравнивает infohash: если
раздача обновилась, в UI появляется дата изменения. Приложение не качает содержимое
раздачи и не управляет торрент-клиентом.

> Авторизации нет. Слушайте приложение только на localhost или в доверенной LAN.
> Пароль рутрекера хранится в SQLite зашифрованным (ключи Data Protection — в `/data/keys`),
> а кука сеанса — в открытом виде в БД и в профиле Chromium (`/data/browser`), как и весь
> каталог `/data`.

## Возможности

- Список отслеживаемых топиков: добавление по ссылке, пауза, удаление.
- Глобальный аккаунт рутрекера: логин и пароль в настройках, автоматический вход браузером.
  Пароль шифруется, кука сеанса кэшируется и в API отдаётся только маской. Есть ручная кука
  как аварийный вариант.
- Автоматический обход проверки Cloudflare на `dl.php`: браузер (Playwright) получает
  `cf_clearance`, сами `.torrent` качает обычный HTTP-клиент. Есть ручной режим и кнопка
  «Пройти проверку Cloudflare».
- Автоскан с глобальным интервалом 1…1440 минут; интервал, аккаунт и автоскан применяются
  без перезапуска.
- Ручная проверка одного торрента (синхронно) и всех включённых (в фоне со статусом).
- Определение обновления по infohash v1, хранение только последнего `.torrent`.
- Веб-интерфейс на Vue 3 + PrimeVue и API на ASP.NET Core.

## Быстрый старт (Docker)

```bash
docker compose up -d --build
```

На Windows можно запустить `start.bat`: он проверит Docker, при необходимости откроет
Docker Desktop, соберёт образ, дождётся healthcheck и откроет UI. Повторный запуск с
`start.bat --no-build` использует готовый образ.

- UI: <http://localhost:8088>
- API: <http://localhost:8088/api>
- OpenAPI/Scalar: <http://localhost:8088/api/docs>

1. Откройте «Настройки», введите логин и пароль рутрекера и нажмите «Сохранить».
2. Нажмите «Войти»: Chromium откроет `login.php`, пройдёт Cloudflare и сохранит сеанс.
   Вход выполняется и автоматически при первом скачивании, если сеанса ещё нет.
3. В блоке «Cloudflare» при необходимости нажмите «Пройти проверку Cloudflare»: браузер
   сохранит пропуск (`cf_clearance`) примерно на год.
4. Нажмите «Проверить сеанс» — проверка скачивает `.torrent` первого отслеживаемого топика;
   для первой проверки добавьте хотя бы один торрент.
5. На экране «Торренты» вставьте ссылку на топик `https://rutracker.org/forum/viewtopic.php?t=…`.

Если рутрекер покажет капчу при входе, автоматический вход не сработает: введите куку сеанса
вручную в «Настройки → Дополнительно» (её можно скопировать из своего браузера).

Данные лежат в `./data`:

```
data/
├─ torrentwatcher.db        # SQLite (WAL)
├─ torrents/{topicId}.torrent
├─ browser/                 # профиль Chromium (куки рутрекера и Cloudflare)
└─ keys/                    # ключи Data Protection для пароля
```

`docker compose down` сохраняет данные; `docker compose down -v` их не удалит, потому что
используется bind-mount `./data` — удалите каталог вручную, если нужен чистый старт.

## Публикация на сервере

`deploy_server.bat` собирает образ локально, выгружает его в tar, копирует на сервер
вместе с шаблоном compose и пересоздаёт контейнер через SSH. Требуются Docker Desktop
и PuTTY (`plink.exe`, `pscp.exe` в `PATH`); модель та же, что в ProxyTunnel.

1. Запустите `deploy_server.bat` из корня репозитория.
2. При первом запуске укажите SSH-хост, пользователя и способ входа (ключ `.ppk` или пароль).
   Настройки сохраняются в `scripts\deploy\server\deploy.local.cfg` (в git не попадает).
3. Скрипт собирает `torrentwatcher:<timestamp>`, загружает образ и вызывает
   `remote-deploy.sh` на сервере.

Войти можно под `root` или под другой УЗ:

- если УЗ не root, скрипт спросит пароль sudo и передаст его на сервер отдельным файлом
  (на сервере права `600`, файл удаляется сразу после чтения; в журнал и историю команд
  он не пишется); пустой пароль означает «настроен NOPASSWD». Пароль можно задать заранее
  переменной окружения `SUDO_PW` — тогда запрос не появится;
- для неинтерактивного деплоя удобнее разрешить sudo без пароля:
  ```bash
  echo 'deploy ALL=(ALL) NOPASSWD: ALL' | sudo tee /etc/sudoers.d/torrentwatcher
  sudo chmod 0440 /etc/sudoers.d/torrentwatcher
  ```
- если sudo нет, но УЗ состоит в группе `docker`, деплой возможен без root: укажите
  `INSTALL_DIR` в домашнем каталоге (например, `/home/deploy/torrentwatcher`), иначе
  не хватит прав на `/torrentwatcher`.

На сервере:

- каталог установки — `/torrentwatcher` (меняется через `INSTALL_DIR` в
  `deploy.local.cfg`): `docker-compose.yaml`, `state/` и `data/`;
- образ передаётся по SSH со сжатием (`pscp -C`): tar с Chromium и .NET ужимается
  примерно в 2–2,5 раза. Отключается переменной `PSCP_COMPRESS=0` перед запуском,
  если сжатие на слабом CPU сервера окажется медленнее;
- UI/API публикуются на **8088** (`APP_PORT`), внутри контейнера Kestrel слушает 8080;
  8080 на хосте остаётся qBittorrent;
- `docker-compose.yaml` создаётся только при первом деплое, дальше правьте его на сервере;
- скрипт сам выбирает `docker compose` (плагин) или `docker-compose` (v1); если нет ни
  одного, деплой остановится с подсказкой, что установить. При использовании v1 в файл
  добавляется `version: "2.4"`, иначе старые версии могут проигнорировать `volumes`;
- права на `/torrentwatcher/data` выставляются пользователю `app` из образа
  (SQLite, `.torrent`, ключи и профиль Chromium пишутся без root).

Если приложение уже работало в другом каталоге, до первого деплоя скопируйте его `data`
в `/torrentwatcher/data` — иначе настройки, ключи и сеанс начнутся с нуля. Старый
контейнер из прежнего `docker compose` тоже нужно остановить (`docker compose down`
в старом каталоге): новый использует фиксированное имя `torrentwatcher` и порт 8088.
Старые образы на сервере чистятся автоматически: остаются `latest`, текущий и предыдущий
теги; предыдущий можно использовать для rollback.

Если передача всё равно медленная (2–3 МБ/с), проверьте:

- реальную скорость линка: гигабит/100 Мбит, Wi-Fi 2,4 ГГц вместо кабеля;
- сравнение `pscp` с `-C` и без него на большом файле;
- CPU сервера: на слабом ARM сжатие может упереться в процессор.

Радикально быстрее повторные деплои делает сборка образа прямо на сервере: на ПК
остаётся только исходники, а тяжёлый слой Chromium переиспользуется из кэша Docker.
Это отдельный режим, которого пока нет.

## Переменные окружения

| Переменная | По умолчанию | Назначение |
|---|---|---|
| `ConnectionStrings__Default` | `Data Source=/data/torrentwatcher.db` | Строка подключения SQLite |
| `Storage__TorrentsDirectory` | `/data/torrents` | Каталог последних `.torrent` |
| `Security__KeysDirectory` | `/data/keys` | Ключи Data Protection (шифрование пароля) |
| `Rutracker__Browser__ProfileDirectory` | `/data/browser` | Профиль Chromium с сеансом рутрекера и куками Cloudflare |
| `Rutracker__Browser__Headless` | `false` | Headless не проходит проверку Cloudflare |
| `Rutracker__Browser__SolveTimeoutSeconds` | `90` | Сколько ждать прохождения челленджа |
| `Rutracker__Browser__DisableSandbox` | `true` | `--no-sandbox` для Chromium в контейнере |
| `Rutracker__Browser__ExecutablePath` | — | Свой Chromium вместо встроенного |
| `ASPNETCORE_URLS` | `http://+:8080` | Адрес Kestrel |
| `APP_PORT` | `8088` | Опубликованный на хосте порт UI/API (внутри контейнера 8080) |
| `ASPNETCORE_ENVIRONMENT` | `Production` | Окружение ASP.NET Core |
| `TZ` | `Europe/Moscow` (compose) | Часовой пояс контейнера |

В локальной разработке `appsettings.Development.json` задаёт `data/torrents` и `data/keys`,
чтобы файлы не попадали в системный `/data`.

## Локальная разработка

Требования: .NET SDK 10.0.401+, Node.js 24+.

```bash
dotnet build
dotnet test

# один раз: браузер для прохождения проверки Cloudflare
pwsh src/TorrentWatcher.Web/bin/Debug/net10.0/playwright.ps1 install chromium

dotnet run --project src/TorrentWatcher.Web   # http://localhost:5080
```

На Linux/macOS вместо PowerShell:

```bash
dotnet src/TorrentWatcher.Web/bin/Debug/net10.0/Microsoft.Playwright.dll install chromium
```

При решении челленджа локально открывается окно Chromium: это ожидаемо, потому что
Cloudflare не пропускает headless-браузер. В контейнере окно рисуется в Xvfb.

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

## Аккаунт рутрекера

Логин и пароль задаются в настройках. Пароль хранится в SQLite зашифрованным: ключи
Data Protection лежат в `/data/keys` (`Security__KeysDirectory`), поэтому копия одной БД
пароль не раскрывает. В API и журнал пароль не попадает никогда.

1. Если кэша куки нет, `RutrackerSessionService` расшифровывает пароль и просит Chromium
   войти на `login.php`. Браузер уже поднимается для Cloudflare, отдельного механизма нет.
2. Полученная кука `bb_session`/`bb_data` сохраняется в настройках и используется обычным
   `HttpClient`; браузер на каждый запрос не запускается.
3. Смена логина или пароля сбрасывает кэш сеанса: следующий запрос войдёт заново.
4. После неудачного входа 10 минут автоматические попытки не повторяются, чтобы не
   спровоцировать блокировку аккаунта. Кнопка «Войти» в настройках паузу игнорирует.
5. Если рутрекер требует капчу, приходит код `RUTRACKER_LOGIN_CAPTCHA_REQUIRED`. В этом
   случае используйте ручную куку в «Настройки → Дополнительно».

## Cloudflare на dl.php

Рутрекер закрыл скачивание вложений managed challenge Cloudflare: без куки `cf_clearance`
`dl.php?t=…` отвечает `403` с заголовком `Cf-Mitigated: challenge`. Приложение обходит это так:

1. `PlaywrightChallengeSolver` запускает Chromium (persistent-профиль в `data/browser`),
   открывает `dl.php` и ждёт, пока Cloudflare выдаст `cf_clearance`.
2. Кука и User-Agent сохраняются в настройках. `cf_clearance` привязан к IP и UA, поэтому
   HTTP-запросы отправляют ровно тот же UA.
3. `CloudflareAwareRutrackerClient` сначала пробует обычный `HttpClient`. Если Cloudflare
   снова вернул челлендж, пропуск решается заново и запрос повторяется один раз.
4. Если HTTP-клиент не пропускают и со свежим пропуском (Cloudflare проверяет ещё и
   браузерный отпечаток), `.torrent` скачивается тем же Chromium: кука рутрекера кладётся
   в его контекст, браузер открывает `dl.php` и отдаёт файл. После этого до перезапуска
   приложения сканы идут сразу через браузер, без лишних 403.
5. Если и браузер не смог — `RUTRACKER_CHALLENGE_REQUIRED` и подсказка в UI, скан
   прерывается без обнуления куки рутрекера.

Особенности:

- Первое скачивание после старта контейнера занимает ~3 с (HTTP → челлендж → браузер),
  последующие — доли секунды, пока живёт Chromium.
- `cf_clearance` живёт около года; профиль браузера и кука переживают перезапуск контейнера.
- В профиле Chromium лежит сеанс рутрекера (вход выполняется тем же браузером): без него
  `dl.php` отвечает `attachment data not found`. Профиль, как и БД, находится в `/data`.
- Headless-Chromium Cloudflare не пропускает: в Docker браузер работает под Xvfb,
  локально окно появляется на экране.
- Аварийный режим: в настройках можно вставить `cf_clearance` и UA вручную из DevTools.
- Обход Cloudflare — гонка вооружений и может нарушать правила сервиса. При агрессивном
  скане есть риск блокировки IP; интервал по умолчанию — 60 минут.

## Архитектура

Три слоя, зависимости строго в одну сторону:

```
Domain ← Application ← Infrastructure
                     ← Web (Infrastructure — только для DI)
```

- `TorrentWatcher.Domain` — сущности, парсер ссылок, bencode и метаданных `.torrent`.
- `TorrentWatcher.Application` — use-cases, порты, DTO, планировщик и координатор сканов.
- `TorrentWatcher.Infrastructure` — EF Core + SQLite, HTTP-клиент, Chromium (Playwright)
  для входа и Cloudflare, Data Protection, файлы.
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
5. Если включено авто-применение, обновление сразу уходит в qBittorrent: старая раздача
   удаляется без файлов, новая добавляется в ту же папку, запускается пересчёт хешей.
6. Две подряд ошибки «403/не .torrent» прерывают run, кэш сеанса помечается невалидным;
   следующий запрос снова входит по логину и паролю.
7. Между запросами выдерживается `RequestDelayMs`; статус доступен в UI и `/api/system/status`.

## qBittorrent

Приложение подключается к Web UI qBittorrent по API v2 и умеет применять найденные
обновления: при смене infohash старая раздача удаляется (`deleteFiles=false`), новая
добавляется в ту же папку с той же категорией и тегами, затем запускается recheck,
чтобы уже скачанные файлы переиспользовались. Настройки — на экране «Настройки»:

- адрес Web UI, логин и пароль (пароль шифруется Data Protection);
- переключатель «Применять обновления автоматически»;
- кнопка «Проверить соединение» (показывает версию клиента или ошибку).

Адрес должен быть доступен **из контейнера** приложения. Если qBittorrent стоит прямо
на хосте, используйте `http://host.docker.internal:8080` на Docker Desktop или добавьте
в compose:

```yaml
extra_hosts:
  - "host.docker.internal:host-gateway"
```

Если qBittorrent в Docker — положите оба контейнера в одну сеть и обращайтесь по имени
сервиса, например `http://qbittorrent:8080`.

В Web UI qBittorrent должны быть включены Web UI (Remote control), вход по логину и паролю
и прослушивание внешних интерфейсов; «Enable Host header validation» нужно выключить или
добавить в список используемый хост, иначе API отвечает `401/403`. Приложение наружу
публикуется на 8088, поэтому порт 8080 остаётся за qBittorrent.

Сбой qBittorrent не делает проверку торрента ошибочной: ошибка сохраняется в записи,
показывается в таблице, и применение повторяется на следующем скане или по кнопке
«Применить». Старые файлы при замене не удаляются. Авто-применение не импортирует
раздачи, которых в клиенте ещё не было: оно реагирует только на обновления (первое
добавление — вручную кнопкой «Применить»). Если старой раздачи в клиенте нет, новая
добавляется с папкой по умолчанию qBittorrent.

## API

Полная схема — `/api/docs`; OpenAPI JSON — `/openapi/v1.json`.

| Метод | Путь | Назначение |
|---|---|---|
| GET | `/api/torrents` | Список и сводка |
| POST | `/api/torrents` | Добавить по `{ "url": "…" }` |
| DELETE | `/api/torrents/{id}` | Убрать и удалить `.torrent` |
| PUT | `/api/torrents/{id}/enabled` | Пауза/возврат в автоскан |
| POST | `/api/torrents/{id}/check` | Проверить один торрент синхронно |
| POST | `/api/torrents/{id}/apply` | Применить обновление в qBittorrent вручную |
| POST | `/api/torrents/check-all` | Запустить проверку всех в фоне |
| GET/PUT | `/api/settings` | Настройки; PUT применяется сразу |
| POST | `/api/settings/rutracker/login` | Войти по сохранённым логину и паролю |
| POST | `/api/settings/test-cookie` | Пробное скачивание (проверка сеанса) |
| POST | `/api/settings/cloudflare/solve` | Пройти проверку Cloudflare браузером |
| POST | `/api/settings/qbittorrent/test` | Проверить соединение с qBittorrent |
| GET | `/api/system/status` | Версия, run, расписание, счётчики, аккаунт/сеанс |
| GET | `/api/health` | Healthcheck (SQLite) |
| GET | `/api/docs` | Scalar UI |

Ошибки — RFC 7807 `ProblemDetails` с полями `code` и `traceId`, без стектрейсов.
Коды: `INVALID_URL`, `INVALID_SETTINGS`, `DUPLICATE_TOPIC`, `TORRENT_NOT_FOUND`,
`TOPIC_NOT_FOUND`, `RUTRACKER_COOKIE_INVALID`, `RUTRACKER_LOGIN_FAILED`,
`RUTRACKER_LOGIN_CAPTCHA_REQUIRED`, `RUTRACKER_CREDENTIALS_UNREADABLE`,
`RUTRACKER_CHALLENGE_REQUIRED`, `RUTRACKER_UNAVAILABLE`, `RUTRACKER_INVALID_PAYLOAD`,
`TORRENT_STORAGE_FAILED`, `QBITTORRENT_NOT_CONFIGURED`, `QBITTORRENT_AUTH_FAILED`,
`QBITTORRENT_UNAVAILABLE`, `SCAN_ALREADY_RUNNING`, `ENDPOINT_NOT_FOUND`, `INTERNAL_ERROR`.

Примеры запросов — `src/TorrentWatcher.Web/TorrentWatcher.Web.http`.

## База данных и миграции

SQLite включается с WAL, `busy_timeout=5000` и `foreign_keys=on`. Миграции накатываются
при старте приложения.

```bash
dotnet ef migrations add <Name> -p src/TorrentWatcher.Infrastructure -s src/TorrentWatcher.Web
dotnet ef database update -p src/TorrentWatcher.Infrastructure -s src/TorrentWatcher.Web
```

## Ограничения

- Нет авторизации и мультипользовательности; один глобальный аккаунт рутрекера.
- Пароль шифруется ключами Data Protection из `/data/keys`; кука сеанса хранится в БД
  plaintext. Файл БД, ключи и `.torrent` доступны владельцу хоста.
- Если рутрекер потребует капчу при входе, автоматический вход остановится с кодом
  `RUTRACKER_LOGIN_CAPTCHA_REQUIRED`; останется ручная кука сеанса.
- Потеря каталога `/data/keys` делает сохранённый пароль нечитаемым — его нужно ввести заново.
- Нет уведомлений (Telegram/ntfy/e-mail) и истории обновлений — только
  `LastChangedAtUtc` и последний статус.
- Интеграция с qBittorrent работает по API v2: Web UI должен быть доступен из контейнера,
  «Enable Host header validation» выключен (или хост добавлен в список), а адрес и учётные
  данные заданы в настройках. Старые файлы при замене раздачи не удаляются, история версий
  по-прежнему не хранится.
- Хранится только последняя версия `.torrent`, не архив.
- Хост рутрекера фиксирован (`rutracker.org`), прокси не поддерживается.
- Cloudflare обходится браузером; HTML раздачи не парсим. Если Cloudflare ужесточит проверку,
  автоматика может перестать работать — тогда остаётся ручной `cf_clearance`.
- Скачивание через Chromium сериализовано: одновременно идёт один браузерный запрос, что
  нормально при интервале скана в минуты.

## Диагностика

```bash
docker compose ps
docker compose logs -f torrentwatcher
curl -i http://localhost:8088/api/health
```

Если контейнер не стартует на Windows с bind-mount, проверьте права на `./data`; при
блокировках SQLite можно перейти на named volume (см. риск в `PLAN.md`).
