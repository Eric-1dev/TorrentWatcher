using TorrentWatcher.Application.TorrentClients;

namespace TorrentWatcher.Application.Abstractions;

/// <summary>
/// Задел под внешний торрент-клиент (qBittorrent). Сейчас реализации нет: приложение
/// только обнаруживает обновление infohash и показывает его в UI. Будущая реализация
/// получает уже скачанный файл и метаданные торрента, поэтому повторно ходить на рутрекер
/// при применении обновления не нужно.
/// </summary>
public interface ITorrentClient
{
    /// <summary>
    /// Идемпотентно добавляет .torrent в клиент: если торрент с этим infohash уже есть,
    /// реализация не должна создавать дубликат.
    /// </summary>
    Task AddTorrentAsync(TorrentClientAddRequest request, CancellationToken cancellationToken);
}
