using TorrentWatcher.Application.Rutracker;

namespace TorrentWatcher.Application.Abstractions;

/// <summary>
/// Транспорт рутрекера без знания про Cloudflare: один HTTP-запрос за .torrent.
/// Отдельный порт нужен, чтобы обёртка с решением челленджа не зависела от <c>HttpClient</c>.
/// </summary>
public interface IRutrackerHttpClient
{
    /// <exception cref="Exceptions.RutrackerException">Рутрекер недоступен, вернул не .torrent
    /// или потребовал проверку Cloudflare.</exception>
    Task<byte[]> DownloadTorrentAsync(
        RutrackerDownloadRequest request,
        CancellationToken cancellationToken);
}
