namespace TorrentWatcher.Application.Storage;

/// <summary>
/// Результат сохранения .torrent на диск. Путь абсолютный, хеш — SHA-256 сырых байтов файла
/// (нужен, чтобы при отладке отличать перезаписанный файл от прежнего).
/// </summary>
/// <param name="FilePath">Абсолютный путь к единственной версии файла.</param>
/// <param name="Sha256">SHA-256 содержимого, 64 hex-символа в нижнем регистре.</param>
/// <param name="SizeBytes">Размер файла в байтах.</param>
/// <param name="StoredAtUtc">Момент сохранения по UTC.</param>
public sealed record StoredTorrentFile(
    string FilePath,
    string Sha256,
    long SizeBytes,
    DateTime StoredAtUtc);
