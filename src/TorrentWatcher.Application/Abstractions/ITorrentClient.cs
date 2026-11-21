using TorrentWatcher.Application.TorrentClients;

namespace TorrentWatcher.Application.Abstractions;

/// <summary>
/// Порт торрент-клиента (qBittorrent). Приложение само скачивает актуальный .torrent
/// с рутрекера и передаёт его клиенту: при смене infohash старая раздача удаляется
/// без удаления файлов, новая добавляется в ту же папку и проверяется.
/// </summary>
public interface ITorrentClient
{
    /// <summary>Проверяет соединение и вход, возвращает версию qBittorrent.</summary>
    /// <exception cref="Exceptions.AppException">
    /// QBITTORRENT_NOT_CONFIGURED, QBITTORRENT_AUTH_FAILED или QBITTORRENT_UNAVAILABLE.
    /// </exception>
    Task<string> GetVersionAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Ищет раздачу по infohash. <c>null</c> — в клиенте её нет.
    /// </summary>
    Task<TorrentClientTorrent?> FindAsync(string infoHash, CancellationToken cancellationToken);

    /// <summary>Добавляет .torrent в клиент.</summary>
    Task AddTorrentAsync(TorrentClientAddRequest request, CancellationToken cancellationToken);

    /// <summary>Удаляет раздачу. Файлы на диске не трогаем: их переиспользует новая версия.</summary>
    Task DeleteTorrentAsync(string infoHash, CancellationToken cancellationToken);

    /// <summary>Запускает пересчёт хешей: qBittorrent подхватывает уже скачанные файлы.</summary>
    Task RecheckAsync(string infoHash, CancellationToken cancellationToken);

    /// <summary>Снимает раздачу с паузы (после добавления или пересчёта).</summary>
    Task ResumeAsync(string infoHash, CancellationToken cancellationToken);
}
