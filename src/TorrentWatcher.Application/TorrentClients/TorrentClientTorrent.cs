namespace TorrentWatcher.Application.TorrentClients;

/// <summary>Раздача, найденная в торрент-клиенте: нужна, чтобы сохранить папку и категорию.</summary>
/// <param name="InfoHash">Infohash v1.</param>
/// <param name="Name">Название раздачи в клиенте.</param>
/// <param name="SavePath">Папка, куда клиент сохраняет содержимое.</param>
/// <param name="Category">Категория; пустая строка — без категории.</param>
/// <param name="Tags">Теги через запятую; пустая строка — без тегов.</param>
/// <param name="State">Состояние qBittorrent, например <c>uploading</c>.</param>
public sealed record TorrentClientTorrent(
    string InfoHash,
    string Name,
    string SavePath,
    string Category,
    string Tags,
    string State);
