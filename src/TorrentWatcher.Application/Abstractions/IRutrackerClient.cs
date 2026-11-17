using TorrentWatcher.Application.Exceptions;
using TorrentWatcher.Application.Rutracker;

namespace TorrentWatcher.Application.Abstractions;

/// <summary>
/// Порт рутрекера: скачивание актуального .torrent по неизменному адресу
/// <c>dl.php?t={topicId}</c>. Реализация — typed <c>HttpClient</c> в Infrastructure.
/// </summary>
public interface IRutrackerClient
{
    /// <summary>Скачивает сырые байты .torrent, не разбирая их.</summary>
    /// <exception cref="RutrackerException">Рутрекер недоступен или вернул не .torrent.</exception>
    Task<byte[]> DownloadTorrentAsync(
        RutrackerDownloadRequest request,
        CancellationToken cancellationToken);
}
