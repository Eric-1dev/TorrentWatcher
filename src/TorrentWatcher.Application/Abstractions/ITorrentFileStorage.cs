using TorrentWatcher.Application.Storage;

namespace TorrentWatcher.Application.Abstractions;

/// <summary>
/// Порт файлового хранилища .torrent. Хранится ровно одна версия на топик:
/// <c>{root}/{topicId}.torrent</c>. Реализация — Infrastructure.
/// </summary>
public interface ITorrentFileStorage
{
    /// <summary>
    /// Атомарно сохраняет .torrent, перезаписывая прежнюю версию: сначала пишется временный
    /// файл рядом с целевым, затем делается rename. Читатель никогда не увидит половину файла.
    /// </summary>
    /// <exception cref="ArgumentException">Пустое содержимое или неположительный topicId.</exception>
    /// <exception cref="Exceptions.AppException">Ошибка ввода-вывода (код <c>TORRENT_STORAGE_FAILED</c>).</exception>
    Task<StoredTorrentFile> SaveAsync(
        int topicId,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken);

    /// <summary>Удаляет файл топика. Возвращает <c>false</c>, если файла уже нет.</summary>
    /// <exception cref="Exceptions.AppException">Ошибка ввода-вывода (код <c>TORRENT_STORAGE_FAILED</c>).</exception>
    Task<bool> DeleteAsync(int topicId, CancellationToken cancellationToken);
}
