namespace TorrentWatcher.Application.Rutracker;

/// <summary>
/// Состояние транспорта рутрекера на время жизни процесса. Cloudflare может не пропускать
/// обычный HTTP-клиент к <c>dl.php</c> даже с действующим пропуском; после первого такого
/// отказа переключаемся на скачивание браузером и больше не тратим запросы впустую.
/// Singleton: значение должно переживать scoped-клиенты и запросы.
/// </summary>
public sealed class CloudflareTransportState
{
    public bool BrowserOnly { get; set; }
}
