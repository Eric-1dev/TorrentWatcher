namespace TorrentWatcher.Domain.Parsing;

/// <summary>
/// Метаданные, которые приложение извлекает из .torrent. HTML рутрекера не парсим:
/// название берём из info, изменение определяем по infohash v1.
/// </summary>
/// <param name="Name">Название торрента (info.name.utf-8 или info.name).</param>
/// <param name="InfoHashV1">SHA-1 info-словаря, 40 hex-символов в нижнем регистре.</param>
/// <param name="SizeBytes">Суммарный размер содержимого.</param>
/// <param name="FileCount">Число файлов (1 для однофайлового торрента).</param>
/// <param name="IsPrivate">Флаг private в info-словаре.</param>
public sealed record TorrentMetadata(
    string Name,
    string InfoHashV1,
    long SizeBytes,
    int FileCount,
    bool IsPrivate);
