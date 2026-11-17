using System.Security.Cryptography;
using TorrentWatcher.Application.Abstractions;
using TorrentWatcher.Application.Exceptions;
using TorrentWatcher.Application.Storage;

namespace TorrentWatcher.UnitTests.Support;

/// <summary>Файловое хранилище в памяти: записывает байты и умеет имитировать сбой диска.</summary>
public sealed class RecordingTorrentFileStorage(DateTimeOffset storedAt) : ITorrentFileStorage
{
    public Dictionary<int, byte[]> Files { get; } = [];

    public bool ThrowOnSave { get; set; }

    public int SaveCount { get; private set; }

    public int DeleteCount { get; private set; }

    public string? LastSavedPath { get; private set; }

    public string? LastSavedSha256 { get; private set; }

    public Task<StoredTorrentFile> SaveAsync(
        int topicId,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        if (ThrowOnSave)
        {
            throw new AppException(ErrorCodes.StorageFailure, "Диск недоступен.");
        }

        byte[] copy = content.ToArray();
        Files[topicId] = copy;
        SaveCount++;

        LastSavedPath = $"/data/torrents/{topicId}.torrent";
        LastSavedSha256 = Convert.ToHexString(SHA256.HashData(copy)).ToLowerInvariant();

        return Task.FromResult(new StoredTorrentFile(
            LastSavedPath,
            LastSavedSha256,
            copy.Length,
            storedAt.UtcDateTime));
    }

    public Task<bool> DeleteAsync(int topicId, CancellationToken cancellationToken)
    {
        DeleteCount++;
        return Task.FromResult(Files.Remove(topicId));
    }
}
