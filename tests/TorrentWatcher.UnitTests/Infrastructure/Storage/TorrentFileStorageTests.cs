using System.Security.Cryptography;
using System.Text;
using TorrentWatcher.Application.Exceptions;
using TorrentWatcher.Application.Storage;
using TorrentWatcher.Infrastructure.Storage;

namespace TorrentWatcher.UnitTests.Infrastructure.Storage;

public sealed class TorrentFileStorageTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 12, 30, 0, TimeSpan.Zero);

    private readonly string _rootDirectory = Path.Combine(
        Path.GetTempPath(),
        "torrentwatcher-storage-tests",
        Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (File.Exists(_rootDirectory))
        {
            File.Delete(_rootDirectory);
        }
        else if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_FirstWrite_CreatesSingleFileAndReturnsMetadata()
    {
        TorrentFileStorage storage = CreateStorage();
        byte[] content = Encoding.UTF8.GetBytes("d8:announce20:https://tracker.test/ae");

        StoredTorrentFile stored = await storage.SaveAsync(123, content, CancellationToken.None);

        string expectedPath = Path.Combine(_rootDirectory, "123.torrent");
        Assert.Equal(expectedPath, stored.FilePath);
        Assert.Equal(content.Length, stored.SizeBytes);
        Assert.Equal(Now.UtcDateTime, stored.StoredAtUtc);
        Assert.Equal(ExpectedSha256(content), stored.Sha256);
        Assert.Equal(content, await File.ReadAllBytesAsync(expectedPath));
        Assert.Equal(expectedPath, Assert.Single(Directory.GetFiles(_rootDirectory, "*.torrent")));
    }

    [Fact]
    public async Task SaveAsync_SecondWrite_ReplacesContentAndLeavesNoTemporaryFiles()
    {
        TorrentFileStorage storage = CreateStorage();
        await storage.SaveAsync(42, Encoding.UTF8.GetBytes("first"), CancellationToken.None);

        byte[] second = Encoding.UTF8.GetBytes("second version");
        StoredTorrentFile stored = await storage.SaveAsync(42, second, CancellationToken.None);

        Assert.Equal(second, await File.ReadAllBytesAsync(stored.FilePath));
        Assert.Equal(ExpectedSha256(second), stored.Sha256);
        Assert.Single(Directory.GetFiles(_rootDirectory, "*.torrent"));
        Assert.Empty(Directory.GetFiles(_rootDirectory, "*.tmp"));
    }

    [Fact]
    public async Task SaveAsync_CreatesMissingRootDirectory()
    {
        TorrentFileStorage storage = CreateStorage();

        StoredTorrentFile stored = await storage.SaveAsync(7, new byte[] { 1, 2, 3 }, CancellationToken.None);

        Assert.True(Directory.Exists(_rootDirectory));
        Assert.True(File.Exists(stored.FilePath));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task SaveAsync_NonPositiveTopicId_Throws(int topicId)
    {
        TorrentFileStorage storage = CreateStorage();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => storage.SaveAsync(topicId, new byte[] { 1 }, CancellationToken.None));
    }

    [Fact]
    public async Task SaveAsync_EmptyContent_Throws()
    {
        TorrentFileStorage storage = CreateStorage();

        await Assert.ThrowsAsync<ArgumentException>(
            () => storage.SaveAsync(1, ReadOnlyMemory<byte>.Empty, CancellationToken.None));
    }

    [Fact]
    public async Task SaveAsync_CanceledBeforeWrite_DoesNotLeaveFiles()
    {
        TorrentFileStorage storage = CreateStorage();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => storage.SaveAsync(9, new byte[4096], cts.Token));

        Assert.Empty(Directory.GetFiles(_rootDirectory));
    }

    [Fact]
    public async Task SaveAsync_IoError_IsWrappedIntoAppException()
    {
        // Каталог хранения занят файлом — создать каталог невозможно.
        Directory.CreateDirectory(Path.GetDirectoryName(_rootDirectory)!);
        await File.WriteAllTextAsync(_rootDirectory, "not a directory");

        TorrentFileStorage storage = CreateStorage();

        AppException exception = await Assert.ThrowsAsync<AppException>(
            () => storage.SaveAsync(5, new byte[] { 1, 2, 3 }, CancellationToken.None));

        Assert.Equal(ErrorCodes.StorageFailure, exception.Code);
        Assert.IsType<IOException>(exception.InnerException);
    }

    [Fact]
    public async Task DeleteAsync_RemovesFileAndReturnsTrue()
    {
        TorrentFileStorage storage = CreateStorage();
        StoredTorrentFile stored = await storage.SaveAsync(11, new byte[] { 1, 2, 3 }, CancellationToken.None);

        Assert.True(await storage.DeleteAsync(11, CancellationToken.None));
        Assert.False(File.Exists(stored.FilePath));
    }

    [Fact]
    public async Task DeleteAsync_MissingFile_ReturnsFalse()
    {
        TorrentFileStorage storage = CreateStorage();

        Assert.False(await storage.DeleteAsync(404, CancellationToken.None));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task DeleteAsync_NonPositiveTopicId_Throws(int topicId)
    {
        TorrentFileStorage storage = CreateStorage();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => storage.DeleteAsync(topicId, CancellationToken.None));
    }

    private TorrentFileStorage CreateStorage() => new(_rootDirectory, new FixedTimeProvider(Now));

    private static string ExpectedSha256(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
