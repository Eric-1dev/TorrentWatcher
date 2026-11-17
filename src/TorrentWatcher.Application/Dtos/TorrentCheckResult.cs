namespace TorrentWatcher.Application.Dtos;

/// <summary>
/// Итог проверки одного торрента. <see cref="ShouldStopScan"/> подсказывает координатору,
/// что серия ответов «403/не .torrent» означает невалидную куку и скан пора прервать.
/// </summary>
/// <param name="Torrent">Состояние торрента после проверки.</param>
/// <param name="Changed">Infohash изменился.</param>
/// <param name="ErrorCode">Машинный код ошибки или <c>null</c> при успехе.</param>
/// <param name="ShouldStopScan">Ошибка указывает на куку/капчу, а не на отдельный топик.</param>
public sealed record TorrentCheckResult(
    TorrentView Torrent,
    bool Changed,
    string? ErrorCode,
    bool ShouldStopScan)
{
    public bool Success => ErrorCode is null;

    public static TorrentCheckResult Failure(
        TorrentView torrent,
        string errorCode,
        bool shouldStopScan) =>
        new(torrent, Changed: false, errorCode, shouldStopScan);
}
