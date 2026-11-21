using TorrentWatcher.Application.Dtos;
using TorrentWatcher.Application.Exceptions;
using TorrentWatcher.Application.Services;
using TorrentWatcher.Application.Settings;
using TorrentWatcher.Application.TorrentClients;
using TorrentWatcher.Domain.Entities;
using TorrentWatcher.UnitTests.Support;

namespace TorrentWatcher.UnitTests.Application.Torrents;

public sealed class TorrentApplyServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly string OldHash = new('a', 40);
    private static readonly string NewHash = new('b', 40);

    [Fact]
    public async Task ApplyAsync_Changed_ReplacesOldTorrentInSameFolder()
    {
        TrackedTorrent torrent = Torrent();
        torrent.SupersededInfoHashV1 = OldHash;
        torrent.AppliedInfoHashV1 = OldHash;

        var client = new RecordingTorrentClient
        {
            FindHandler = hash => hash == OldHash
                ? new TorrentClientTorrent(hash, "Старая", "/downloads/Раздача", "movies", "tag1", "uploading")
                : null,
        };

        var repository = new InMemoryTrackedTorrentRepository(torrent);
        TorrentApplyService service = CreateService(repository, client);

        TorrentApplyResult result = await service.ApplyAsync(torrent, force: false, CancellationToken.None);

        Assert.True(result.Applied);
        Assert.True(result.Success);
        Assert.Equal(
            [
                $"find:{OldHash}",
                $"delete:{OldHash}",
                $"find:{NewHash}",
                $"add:{NewHash}",
                $"recheck:{NewHash}",
                $"resume:{NewHash}",
            ],
            client.Calls);

        Assert.Equal([OldHash], client.Deleted);
        Assert.NotNull(client.LastAdd);
        Assert.Equal("/downloads/Раздача", client.LastAdd.SavePath);
        Assert.Equal("movies", client.LastAdd.Category);
        Assert.Equal("tag1", client.LastAdd.Tags);
        Assert.False(client.LastAdd.Paused);

        TrackedTorrent updated = repository.Torrents[0];
        Assert.Equal(NewHash, updated.AppliedInfoHashV1);
        Assert.Equal(Now.UtcDateTime, updated.AppliedAtUtc);
        Assert.Null(updated.SupersededInfoHashV1);
        Assert.Null(updated.ApplyError);
    }

    [Fact]
    public async Task ApplyAsync_OldTorrentNotInClient_AddsWithoutSavePath()
    {
        TrackedTorrent torrent = Torrent();
        torrent.SupersededInfoHashV1 = OldHash;

        var client = new RecordingTorrentClient { FindHandler = _ => null };
        TorrentApplyService service = CreateService(new InMemoryTrackedTorrentRepository(torrent), client);

        TorrentApplyResult result = await service.ApplyAsync(torrent, force: false, CancellationToken.None);

        Assert.True(result.Applied);
        Assert.Empty(client.Deleted);
        Assert.NotNull(client.LastAdd);
        Assert.Null(client.LastAdd.SavePath);
    }

    [Fact]
    public async Task ApplyAsync_OldHashUnknown_ReplacesTorrentWithSameName()
    {
        TrackedTorrent torrent = Torrent();
        torrent.SupersededInfoHashV1 = null;
        torrent.AppliedInfoHashV1 = null;

        var client = new RecordingTorrentClient();
        client.Torrents.Add(new TorrentClientTorrent(
            OldHash,
            torrent.Title,
            "/downloads/Раздача",
            "movies",
            "tag1",
            "stalledUP"));

        var repository = new InMemoryTrackedTorrentRepository(torrent);
        TorrentApplyService service = CreateService(repository, client);

        TorrentApplyResult result = await service.ApplyAsync(torrent, force: true, CancellationToken.None);

        Assert.True(result.Applied);
        Assert.Equal(
            [
                $"findByName:{torrent.Title}",
                $"delete:{OldHash}",
                $"find:{NewHash}",
                $"add:{NewHash}",
                $"recheck:{NewHash}",
                $"resume:{NewHash}",
            ],
            client.Calls);

        Assert.NotNull(client.LastAdd);
        Assert.Equal("/downloads/Раздача", client.LastAdd.SavePath);
        Assert.Equal("movies", client.LastAdd.Category);
        Assert.Equal("tag1", client.LastAdd.Tags);

        TrackedTorrent updated = repository.Torrents[0];
        Assert.Equal(NewHash, updated.AppliedInfoHashV1);
        Assert.Null(updated.SupersededInfoHashV1);
    }

    [Fact]
    public async Task ApplyAsync_SameNameWithCurrentHash_DoesNotDeleteOrAdd()
    {
        TrackedTorrent torrent = Torrent();
        torrent.SupersededInfoHashV1 = null;

        var client = new RecordingTorrentClient();
        client.Torrents.Add(new TorrentClientTorrent(
            NewHash,
            torrent.Title,
            "/downloads/Раздача",
            "movies",
            "tag1",
            "uploading"));

        var repository = new InMemoryTrackedTorrentRepository(torrent);
        TorrentApplyService service = CreateService(repository, client);

        TorrentApplyResult result = await service.ApplyAsync(torrent, force: true, CancellationToken.None);

        Assert.True(result.Applied);
        Assert.Equal(
            [
                $"findByName:{torrent.Title}",
                $"find:{NewHash}",
            ],
            client.Calls);

        Assert.Empty(client.Deleted);
        Assert.Null(client.LastAdd);
        Assert.Equal(NewHash, repository.Torrents[0].AppliedInfoHashV1);
    }

    [Fact]
    public async Task ApplyAsync_AlreadyApplied_SkipsClient()
    {
        TrackedTorrent torrent = Torrent();
        torrent.AppliedInfoHashV1 = NewHash;

        var client = new RecordingTorrentClient();
        TorrentApplyService service = CreateService(new InMemoryTrackedTorrentRepository(torrent), client);

        TorrentApplyResult result = await service.ApplyAsync(torrent, force: false, CancellationToken.None);

        Assert.False(result.Applied);
        Assert.True(result.Success);
        Assert.Empty(client.Calls);
    }

    [Fact]
    public async Task ApplyAsync_NeverAppliedAutomatically_SkipsClient()
    {
        TrackedTorrent torrent = Torrent();
        torrent.AppliedInfoHashV1 = null;

        var client = new RecordingTorrentClient();
        TorrentApplyService service = CreateService(new InMemoryTrackedTorrentRepository(torrent), client);

        TorrentApplyResult result = await service.ApplyAsync(torrent, force: false, CancellationToken.None);

        Assert.False(result.Applied);
        Assert.True(result.Success);
        Assert.Empty(client.Calls);
    }

    [Fact]
    public async Task ApplyAsync_AutoApplyDisabled_SkipsClient()
    {
        TrackedTorrent torrent = Torrent();
        torrent.SupersededInfoHashV1 = OldHash;

        var client = new RecordingTorrentClient();
        TorrentApplyService service = CreateService(
            new InMemoryTrackedTorrentRepository(torrent),
            client,
            autoApply: false);

        TorrentApplyResult result = await service.ApplyAsync(torrent, force: false, CancellationToken.None);

        Assert.False(result.Applied);
        Assert.True(result.Success);
        Assert.Empty(client.Calls);
    }

    [Fact]
    public async Task ApplyAsync_NotConfiguredAutomatic_SkipsClientAndDoesNotThrow()
    {
        TrackedTorrent torrent = Torrent();
        var client = new RecordingTorrentClient();
        TorrentApplyService service = CreateService(
            new InMemoryTrackedTorrentRepository(torrent),
            client,
            configured: false);

        TorrentApplyResult result = await service.ApplyAsync(torrent, force: false, CancellationToken.None);

        Assert.False(result.Applied);
        Assert.True(result.Success);
        Assert.Empty(client.Calls);
    }

    [Fact]
    public async Task ApplyAsync_NotConfiguredManual_Throws()
    {
        TrackedTorrent torrent = Torrent();
        TorrentApplyService service = CreateService(
            new InMemoryTrackedTorrentRepository(torrent),
            new RecordingTorrentClient(),
            configured: false);

        AppException exception = await Assert.ThrowsAsync<AppException>(
            () => service.ApplyAsync(torrent, force: true, CancellationToken.None));

        Assert.Equal(ErrorCodes.QbittorrentNotConfigured, exception.Code);
    }

    [Fact]
    public async Task ApplyAsync_ClientFailure_RecordsErrorWithoutThrowing()
    {
        TrackedTorrent torrent = Torrent();
        torrent.SupersededInfoHashV1 = OldHash;

        var client = new RecordingTorrentClient
        {
            FindHandler = _ => null,
            FailOnAdd = true,
        };

        var repository = new InMemoryTrackedTorrentRepository(torrent);
        TorrentApplyService service = CreateService(repository, client);

        TorrentApplyResult result = await service.ApplyAsync(torrent, force: false, CancellationToken.None);

        Assert.False(result.Applied);
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.QbittorrentUnavailable, result.ErrorCode);

        TrackedTorrent updated = repository.Torrents[0];
        Assert.NotNull(updated.ApplyError);
        Assert.Null(updated.AppliedInfoHashV1);
        Assert.Equal(OldHash, updated.SupersededInfoHashV1);
    }

    [Fact]
    public async Task ApplyByIdAsync_UnknownId_ThrowsTorrentNotFound()
    {
        TorrentApplyService service = CreateService(
            new InMemoryTrackedTorrentRepository(),
            new RecordingTorrentClient());

        AppException exception = await Assert.ThrowsAsync<AppException>(
            () => service.ApplyByIdAsync(404, CancellationToken.None));

        Assert.Equal(ErrorCodes.TorrentNotFound, exception.Code);
    }

    private static TorrentApplyService CreateService(
        InMemoryTrackedTorrentRepository repository,
        RecordingTorrentClient client,
        bool configured = true,
        bool autoApply = true) =>
        new(
            repository,
            client,
            Settings(configured, autoApply),
            new FixedTimeProvider(Now));

    private static SettingsProvider Settings(bool configured, bool autoApply)
    {
        var provider = new SettingsProvider();
        provider.Apply(AppSettingsSnapshot.FromEntity(new AppSetting
        {
            QbittorrentUrl = configured ? "http://plex.local:8080" : string.Empty,
            QbittorrentUsername = configured ? "admin" : string.Empty,
            QbittorrentPasswordProtected = configured ? "enc:secret" : string.Empty,
            QbittorrentAutoApply = autoApply,
            RequestTimeoutSeconds = 10,
            UpdatedAtUtc = Now.UtcDateTime,
        }));

        return provider;
    }

    private static TrackedTorrent Torrent() => new()
    {
        Id = 1,
        TopicId = 555,
        TopicUrl = "https://rutracker.org/forum/viewtopic.php?t=555",
        Title = "Торрент",
        InfoHashV1 = NewHash,
        TorrentSizeBytes = 1,
        FileCount = 1,
        TorrentFilePath = "/data/torrents/555.torrent",
        TorrentFileSha256 = new string('0', 64),
        TorrentFileDownloadedAtUtc = Now.UtcDateTime.AddMinutes(-5),
        LastCheckOutcome = TorrentCheckOutcome.Changed,
        IsEnabled = true,
        CreatedAtUtc = Now.UtcDateTime.AddDays(-1),
        UpdatedAtUtc = Now.UtcDateTime.AddMinutes(-5),
    };
}
