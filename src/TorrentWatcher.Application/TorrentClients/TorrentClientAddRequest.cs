namespace TorrentWatcher.Application.TorrentClients;

/// <summary>
/// Данные, которые приложение уже знает к моменту «обнаружено обновление»:
/// метаданные из <c>TrackedTorrent</c> и путь к последнему .torrent на диске.
/// </summary>
/// <param name="TopicId">Идентификатор топика рутрекера.</param>
/// <param name="Title">Название из info-словаря .torrent.</param>
/// <param name="InfoHashV1">Infohash v1, 40 hex-символов.</param>
/// <param name="TorrentFilePath">Путь к сохранённому .torrent.</param>
/// <param name="SavePath">Куда сохранять содержимое; <c>null</c> — путь клиента по умолчанию.</param>
/// <param name="Paused">Добавить на паузу.</param>
/// <param name="Category">Категория qBittorrent, копируется со старой раздачи.</param>
/// <param name="Tags">Теги qBittorrent, копируются со старой раздачи.</param>
public sealed record TorrentClientAddRequest(
    int TopicId,
    string Title,
    string InfoHashV1,
    string TorrentFilePath,
    string? SavePath = null,
    bool Paused = false,
    string? Category = null,
    string? Tags = null);
