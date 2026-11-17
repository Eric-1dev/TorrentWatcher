namespace TorrentWatcher.Application.Rutracker;

/// <summary>
/// Параметры одного скачивания .torrent. Кука и таймаут передаются явно: настройки
/// применяются на лету и не должны кешироваться внутри HTTP-клиента.
/// </summary>
/// <param name="TopicId">Идентификатор топика рутрекера (положительный).</param>
/// <param name="Cookie">Строка cookie вида <c>bb_session=…; bb_data=…</c>.</param>
/// <param name="Timeout">Таймаут запроса из настроек; неположительное значение заменяется значением по умолчанию.</param>
public sealed record RutrackerDownloadRequest(int TopicId, string Cookie, TimeSpan Timeout);
