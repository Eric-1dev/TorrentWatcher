using System.Security.Cryptography;
using TorrentWatcher.Application.Abstractions;
using TorrentWatcher.Application.Exceptions;
using TorrentWatcher.Application.Storage;

namespace TorrentWatcher.Infrastructure.Storage;

/// <summary>
/// Файловое хранилище .torrent: одна версия на топик. Запись идёт во временный файл
/// в том же каталоге, затем <see cref="File.Move(string, string, bool)"/> атомарно
/// заменяет целевой — на диске не бывает двух версий и недописанных файлов.
/// </summary>
public sealed class TorrentFileStorage : ITorrentFileStorage
{
    private const string FileExtension = ".torrent";

    private readonly string _rootDirectory;
    private readonly TimeProvider _timeProvider;

    public TorrentFileStorage(string rootDirectory, TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _rootDirectory = Path.GetFullPath(rootDirectory);
        _timeProvider = timeProvider;
    }

    /// <summary>Каталог хранения в нормализованном абсолютном виде.</summary>
    public string RootDirectory => _rootDirectory;

    public async Task<StoredTorrentFile> SaveAsync(
        int topicId,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        EnsureTopicId(topicId);

        if (content.IsEmpty)
        {
            throw new ArgumentException("Содержимое .torrent пустое.", nameof(content));
        }

        string targetPath = GetPath(topicId);

        // Уникальное имя: даже если два сохранения случайно пересекутся, они не затрут
        // временные файлы друг друга, а целевой файл заменится атомарно.
        string tempPath = $"{targetPath}.{Guid.NewGuid():N}.tmp";

        try
        {
            Directory.CreateDirectory(_rootDirectory);

            await using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81_920,
                FileOptions.Asynchronous))
            {
                await stream.WriteAsync(content, cancellationToken);
                await stream.FlushAsync(cancellationToken);

                // rename защищает от «половины файла», fsync — от пустого файла после сбоя питания.
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, targetPath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TryDelete(tempPath);

            throw new AppException(
                ErrorCodes.StorageFailure,
                $"Не удалось сохранить .torrent для топика {topicId}.",
                exception);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }

        string sha256 = Convert.ToHexString(SHA256.HashData(content.Span)).ToLowerInvariant();

        return new StoredTorrentFile(
            targetPath,
            sha256,
            content.Length,
            _timeProvider.GetUtcNow().UtcDateTime);
    }

    public Task<bool> DeleteAsync(int topicId, CancellationToken cancellationToken)
    {
        EnsureTopicId(topicId);
        cancellationToken.ThrowIfCancellationRequested();

        string path = GetPath(topicId);

        try
        {
            if (!File.Exists(path))
            {
                return Task.FromResult(false);
            }

            File.Delete(path);
            return Task.FromResult(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new AppException(
                ErrorCodes.StorageFailure,
                $"Не удалось удалить .torrent для топика {topicId}.",
                exception);
        }
    }

    private string GetPath(int topicId) =>
        Path.Combine(_rootDirectory, topicId.ToString(System.Globalization.CultureInfo.InvariantCulture) + FileExtension);

    private static void EnsureTopicId(int topicId)
    {
        if (topicId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(topicId),
                topicId,
                "Идентификатор топика должен быть положительным.");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Временный файл имеет расширение .tmp и не считается версией .torrent.
            // Исходную ошибку сохранения это не должно затирать.
        }
    }
}
