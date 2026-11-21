using TorrentWatcher.Application.Rutracker;

namespace TorrentWatcher.Application.Abstractions;

/// <summary>
/// Скачивание <c>.torrent</c> браузером. Нужен там, где Cloudflare не пропускает обычный
/// HTTP-клиент даже с действующим <c>cf_clearance</c> (проверка привязана к браузеру).
/// </summary>
public interface IBrowserTorrentDownloader
{
    /// <summary>
    /// Скачивает <c>dl.php?t={topicId}</c> в Chromium с кукой рутрекера и возвращает байты
    /// вместе с актуальным пропуском Cloudflare (если его удалось прочитать).
    /// </summary>
    /// <exception cref="Exceptions.RutrackerException">
    /// Cloudflare не пропустил, кука недействительна или рутрекер вернул не <c>.torrent</c>.
    /// </exception>
    Task<BrowserTorrentDownloadResult> DownloadTorrentAsync(
        int topicId,
        string rutrackerCookie,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

/// <summary>Результат скачивания через браузер.</summary>
public sealed record BrowserTorrentDownloadResult(byte[] Payload, CloudflareClearance? Clearance);
