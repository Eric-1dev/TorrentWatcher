using System.Text;
using TorrentWatcher.Application.Abstractions;
using TorrentWatcher.Application.Dtos;
using TorrentWatcher.Application.Exceptions;
using TorrentWatcher.Application.Services;
using TorrentWatcher.Application.Settings;
using TorrentWatcher.Domain;
using TorrentWatcher.Domain.Entities;
using TorrentWatcher.UnitTests.Domain.Parsing;
using TorrentWatcher.UnitTests.Support;

namespace TorrentWatcher.UnitTests.Application.Torrents;

public sealed class TorrentServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AddAsync_Valid_StoresFileMetadataAndReturnsView()
    {
        (byte[] payload, byte[] info) = TorrentFixtures.SingleFile("Фильм.2024.mkv", length: 2_048);
        var repository = new InMemoryTrackedTorrentRepository();
        var storage = new RecordingTorrentFileStorage(Now);
        var rutracker = new StubRutrackerClient(_ => Task.FromResult(payload));
        TorrentService service = CreateService(repository, storage, rutracker, SettingsWithCookie());

        TorrentView view = await service.AddAsync("https://rutracker.org/forum/viewtopic.php?t=123456", CancellationToken.None);

        Assert.Equal(123456, view.TopicId);
        Assert.Equal(RutrackerUrls.Topic(123456), view.TopicUrl);
        Assert.Equal("Фильм.2024.mkv", view.Title);
        Assert.Equal(TorrentFixtures.ExpectedInfoHash(info), view.InfoHashV1);
        Assert.Equal(2_048, view.TorrentSizeBytes);
        Assert.Equal(1, view.FileCount);
        Assert.True(view.IsEnabled);
        Assert.Equal(TorrentCheckOutcome.Unchanged, view.LastCheckOutcome);
        Assert.Null(view.LastChangedAtUtc);
        Assert.Equal(Now.UtcDateTime, view.LastCheckedAtUtc);
        Assert.Equal(Now.UtcDateTime, view.CreatedAtUtc);
        Assert.Equal(Now.UtcDateTime, view.UpdatedAtUtc);

        Assert.Equal(payload, storage.Files[123456]);

        TrackedTorrent stored = Assert.Single(repository.Torrents);
        Assert.Equal(storage.LastSavedPath, stored.TorrentFilePath);
        Assert.Equal(storage.LastSavedSha256, stored.TorrentFileSha256);

        var request = Assert.Single(rutracker.Requests);
        Assert.Equal(123456, request.TopicId);
        Assert.Equal("bb_session=secret", request.Cookie);
        Assert.Equal(TimeSpan.FromSeconds(30), request.Timeout);
    }

    [Fact]
    public async Task AddAsync_InvalidUrl_ThrowsInvalidUrlWithoutDownloading()
    {
        var rutracker = new StubRutrackerClient(_ => Task.FromResult(Array.Empty<byte>()));
        TorrentService service = CreateService(
            new InMemoryTrackedTorrentRepository(),
            new RecordingTorrentFileStorage(Now),
            rutracker,
            SettingsWithCookie());

        AppException exception = await Assert.ThrowsAsync<AppException>(
            () => service.AddAsync("не ссылка", CancellationToken.None));

        Assert.Equal(ErrorCodes.InvalidUrl, exception.Code);
        Assert.Empty(rutracker.Requests);
    }

    [Fact]
    public async Task AddAsync_DuplicateTopic_ThrowsBeforeDownloading()
    {
        var repository = new InMemoryTrackedTorrentRepository(Torrent(1, 555));
        var rutracker = new StubRutrackerClient(_ => Task.FromResult(Array.Empty<byte>()));
        TorrentService service = CreateService(
            repository,
            new RecordingTorrentFileStorage(Now),
            rutracker,
            SettingsWithCookie());

        AppException exception = await Assert.ThrowsAsync<AppException>(
            () => service.AddAsync("dl.php?t=555", CancellationToken.None));

        Assert.Equal(ErrorCodes.DuplicateTopic, exception.Code);
        Assert.Empty(rutracker.Requests);
    }

    [Fact]
    public async Task AddAsync_NoCookie_ThrowsCookieInvalid()
    {
        var rutracker = new StubRutrackerClient(_ => Task.FromResult(Array.Empty<byte>()));
        TorrentService service = CreateService(
            new InMemoryTrackedTorrentRepository(),
            new RecordingTorrentFileStorage(Now),
            rutracker,
            SettingsWithCookie(cookie: string.Empty));

        // Сеанс бросает RutrackerException — наследника AppException с тем же кодом.
        AppException exception = await Assert.ThrowsAnyAsync<AppException>(
            () => service.AddAsync("123456", CancellationToken.None));

        Assert.Equal(ErrorCodes.RutrackerCookieInvalid, exception.Code);
        Assert.Empty(rutracker.Requests);
    }

    [Fact]
    public async Task AddAsync_BrokenTorrent_ThrowsInvalidPayloadWithoutSaving()
    {
        var storage = new RecordingTorrentFileStorage(Now);
        TorrentService service = CreateService(
            new InMemoryTrackedTorrentRepository(),
            storage,
            new StubRutrackerClient(_ => Task.FromResult(Encoding.ASCII.GetBytes("<html>капча</html>"))),
            SettingsWithCookie());

        AppException exception = await Assert.ThrowsAsync<AppException>(
            () => service.AddAsync("123456", CancellationToken.None));

        Assert.Equal(ErrorCodes.RutrackerInvalidPayload, exception.Code);
        Assert.Empty(storage.Files);
    }

    [Fact]
    public async Task AddAsync_StorageFailure_PropagatesAndKeepsRepositoryEmpty()
    {
        (byte[] payload, _) = TorrentFixtures.SingleFile();
        var repository = new InMemoryTrackedTorrentRepository();
        var storage = new RecordingTorrentFileStorage(Now) { ThrowOnSave = true };
        TorrentService service = CreateService(
            repository,
            storage,
            new StubRutrackerClient(_ => Task.FromResult(payload)),
            SettingsWithCookie());

        AppException exception = await Assert.ThrowsAsync<AppException>(
            () => service.AddAsync("123456", CancellationToken.None));

        Assert.Equal(ErrorCodes.StorageFailure, exception.Code);
        Assert.Empty(repository.Torrents);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsItemsAndUpdateSummary()
    {
        var repository = new InMemoryTrackedTorrentRepository(
            Torrent(1, 10, TorrentCheckOutcome.Changed),
            Torrent(2, 20, TorrentCheckOutcome.Unchanged),
            Torrent(3, 30, TorrentCheckOutcome.Error),
            Torrent(4, 40, TorrentCheckOutcome.Changed));
        TorrentService service = CreateService(
            repository,
            new RecordingTorrentFileStorage(Now),
            new StubRutrackerClient(_ => Task.FromResult(Array.Empty<byte>())),
            SettingsWithCookie());

        TorrentListResult result = await service.GetAllAsync(CancellationToken.None);

        Assert.Equal(4, result.TotalCount);
        Assert.Equal(2, result.UpdateAvailableCount);
        Assert.True(result.HasUpdates);
        Assert.Equal(new[] { 1, 2, 3, 4 }, result.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task GetAllAsync_AppliedChange_IsNotCountedAsPending()
    {
        TrackedTorrent applied = Torrent(1, 10, TorrentCheckOutcome.Changed);
        applied.AppliedInfoHashV1 = applied.InfoHashV1;

        var repository = new InMemoryTrackedTorrentRepository(
            applied,
            Torrent(2, 20, TorrentCheckOutcome.Changed));

        TorrentService service = CreateService(
            repository,
            new RecordingTorrentFileStorage(Now),
            new StubRutrackerClient(_ => Task.FromResult(Array.Empty<byte>())),
            SettingsWithCookie());

        TorrentListResult result = await service.GetAllAsync(CancellationToken.None);

        Assert.Equal(1, result.UpdateAvailableCount);
    }

    [Fact]
    public async Task DeleteAsync_RemovesFileAndRecord()
    {
        var storage = new RecordingTorrentFileStorage(Now);
        storage.Files[555] = [1, 2, 3];
        var repository = new InMemoryTrackedTorrentRepository(Torrent(1, 555));
        TorrentService service = CreateService(
            repository,
            storage,
            new StubRutrackerClient(_ => Task.FromResult(Array.Empty<byte>())),
            SettingsWithCookie());

        await service.DeleteAsync(1, CancellationToken.None);

        Assert.Empty(repository.Torrents);
        Assert.False(storage.Files.ContainsKey(555));
    }

    [Fact]
    public async Task DeleteAsync_FileAlreadyMissing_StillDeletesRecord()
    {
        var repository = new InMemoryTrackedTorrentRepository(Torrent(1, 555));
        TorrentService service = CreateService(
            repository,
            new RecordingTorrentFileStorage(Now),
            new StubRutrackerClient(_ => Task.FromResult(Array.Empty<byte>())),
            SettingsWithCookie());

        await service.DeleteAsync(1, CancellationToken.None);

        Assert.Empty(repository.Torrents);
    }

    [Fact]
    public async Task DeleteAsync_UnknownId_ThrowsTorrentNotFound()
    {
        TorrentService service = CreateService(
            new InMemoryTrackedTorrentRepository(),
            new RecordingTorrentFileStorage(Now),
            new StubRutrackerClient(_ => Task.FromResult(Array.Empty<byte>())),
            SettingsWithCookie());

        AppException exception = await Assert.ThrowsAsync<AppException>(
            () => service.DeleteAsync(42, CancellationToken.None));

        Assert.Equal(ErrorCodes.TorrentNotFound, exception.Code);
    }

    [Fact]
    public async Task SetEnabledAsync_TogglesAndPersistsTimestamp()
    {
        TrackedTorrent torrent = Torrent(1, 555);
        var repository = new InMemoryTrackedTorrentRepository(torrent);
        TorrentService service = CreateService(
            repository,
            new RecordingTorrentFileStorage(Now),
            new StubRutrackerClient(_ => Task.FromResult(Array.Empty<byte>())),
            SettingsWithCookie());

        TorrentView view = await service.SetEnabledAsync(1, isEnabled: false, CancellationToken.None);

        Assert.False(view.IsEnabled);
        Assert.False(repository.Torrents[0].IsEnabled);
        Assert.Equal(Now.UtcDateTime, repository.Torrents[0].UpdatedAtUtc);
    }

    [Fact]
    public async Task SetEnabledAsync_UnknownId_ThrowsTorrentNotFound()
    {
        TorrentService service = CreateService(
            new InMemoryTrackedTorrentRepository(),
            new RecordingTorrentFileStorage(Now),
            new StubRutrackerClient(_ => Task.FromResult(Array.Empty<byte>())),
            SettingsWithCookie());

        AppException exception = await Assert.ThrowsAsync<AppException>(
            () => service.SetEnabledAsync(7, isEnabled: true, CancellationToken.None));

        Assert.Equal(ErrorCodes.TorrentNotFound, exception.Code);
    }

    private static TorrentService CreateService(
        ITrackedTorrentRepository repository,
        ITorrentFileStorage storage,
        IRutrackerClient rutracker,
        ISettingsProvider settingsProvider) =>
        new(
            repository,
            rutracker,
            StubRutrackerSessionProvider.FromSettings(settingsProvider),
            storage,
            settingsProvider,
            new FixedTimeProvider(Now));

    private static SettingsProvider SettingsWithCookie(
        string cookie = "bb_session=secret",
        int timeoutSeconds = 30)
    {
        var provider = new SettingsProvider();
        provider.Apply(AppSettingsSnapshot.FromEntity(new AppSetting
        {
            RutrackerCookie = cookie,
            RequestTimeoutSeconds = timeoutSeconds,
            UpdatedAtUtc = Now.UtcDateTime,
        }));

        return provider;
    }

    private static TrackedTorrent Torrent(
        int id,
        int topicId,
        TorrentCheckOutcome outcome = TorrentCheckOutcome.Unchanged) => new()
        {
            Id = id,
            TopicId = topicId,
            TopicUrl = RutrackerUrls.Topic(topicId),
            Title = $"Торрент {topicId}",
            InfoHashV1 = "abc",
            LastCheckOutcome = outcome,
            IsEnabled = true,
            CreatedAtUtc = Now.UtcDateTime,
            UpdatedAtUtc = Now.UtcDateTime,
        };
}
