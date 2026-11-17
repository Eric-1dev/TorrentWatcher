# Как добавить qBittorrent позже

Сейчас приложение только обнаруживает обновление `.torrent` по infohash и показывает
дату в UI. Ниже — план точечного расширения, не требующего переделывать скан.

## Что уже есть

- `TrackedTorrent` хранит `TopicId`, `Title`, `InfoHashV1`, размер, число файлов,
  `TorrentFilePath`, SHA-256 файла, дату скачивания и `LastChangedAtUtc`.
- Последний `.torrent` лежит в `{Storage:TorrentsDirectory}/{topicId}.torrent`.
- `TorrentScanService` при смене infohash выставляет `LastCheckOutcome = Changed`
  и обновляет дату; файл к этому моменту уже перезаписан атомарно.
- Порт `ITorrentClient` (`Application/Abstractions/ITorrentClient.cs`) и
  `TorrentClientAddRequest` описывают идемпотентное добавление файла в клиент.
- Настройки читаются «на лету» через `ISettingsProvider`; тот же механизм подойдёт
  для URL/логина qBittorrent.

## Шаги

1. **Настройки.** Добавить в `AppSetting` поля `QbittorrentUrl`, `QbittorrentUsername`,
   `QbittorrentPassword` (или API key), `QbittorrentCategory`, `QbittorrentSavePath`,
   `AutoApplyUpdates`. Секреты в API маскировать так же, как куку. Нужна миграция.
2. **Клиент.** Реализовать `ITorrentClient` в `Infrastructure/Qbittorrent/`:
   typed `HttpClient`, `POST /api/v2/auth/login`, `POST /api/v2/torrents/add`
   (`multipart/form-data` с файлом), проверка `GET /api/v2/torrents/info?hashes=…`
   для идемпотентности. Ошибки — `AppException` с новым кодом (например,
   `QBITTORRENT_UNAVAILABLE`), чтобы middleware отдал ProblemDetails.
3. **Регистрация.** В `DependencyInjection.AddInfrastructure` зарегистрировать реализацию,
   только если qBittorrent настроен; иначе оставить `null`/`NullTorrentClient`.
4. **Применение.** После `Changed` в `TorrentScanService` (или отдельным
   `UpdateApplier`) вызвать `ITorrentClient.AddTorrentAsync` с путём к файлу и infohash.
   Хранить признак применения: добавить `AppliedInfoHashV1` и `AppliedAtUtc`, чтобы
   повторный скан не добавлял тот же торрент снова и UI мог показать
   «обновление обнаружено» / «обновление применено».
5. **Отказоустойчивость.** Сетевые ошибки qBittorrent не должны переводить проверку
   в `Error`: скачивание уже успешно. Повторять применение с backoff или при следующем
   скане; при старте — сверять неприменённые `Changed`-записи.
6. **UI.** Добавить на экран «Настройки» блок qBittorrent и кнопку проверки соединения;
   в таблице — бейдж «применено» и, при желании, кнопку «применить сейчас».

## Границы

- Рутрекер остаётся единственным источником `.torrent`; qBittorrent не скачивает файл
  сам и не требует куки рутрекера.
- История версий по-прежнему не нужна: хранится только последний файл и хеши
  (текущий и, после расширения, применённый).
- Авторизация приложения не появляется; qBittorrent-креды — такие же plaintext-настройки,
  как кука, и защищаются только доступом к хосту/`/data`.
