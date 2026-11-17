using System.Text;
using TorrentWatcher.Application.Abstractions;
using TorrentWatcher.Application.Dtos;
using TorrentWatcher.Application.Exceptions;
using TorrentWatcher.Application.Services;
using TorrentWatcher.Application.Settings;
using TorrentWatcher.Domain.Entities;
using TorrentWatcher.Domain.Parsing;
using TorrentWatcher.UnitTests.Domain.Parsing;
using TorrentWatcher.UnitTests.Support;

namespace TorrentWatcher.UnitTests.Application.Torrents;

public sealed class TorrentScanServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 17, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CheckAsync_SameInfoHash_RecordsUnchangedAndClearsError()
    {
        (byte[] payload, byte[] info) = TorrentFixtures.SingleFile("Название из файла", length: 4_096);
        TorrentMetadata metadata = TorrentMetadataParser.Parse(payload);

        TrackedTorrent torrent = Torrent();
        torrent.InfoHashV1 = TorrentFixtures.ExpectedInfoHash(info);
        torrent.LastCheckOutcome = TorrentCheckOutcome.Never;
        torrent.LastError = "старая ошибка";

        var repository = new InMemoryTrackedTorrentRepository(torrent);
        var storage = new RecordingTorrentFileStorage(Now);
        var rutracker = new StubRutrackerClient(_ => Task.FromResult(payload));
        TorrentScanService service = CreateService(repository, storage, rutracker);

        TorrentCheckResult result = await service.CheckAsync(torrent.Id, CancellationToken.None);

        Assert.True(result.Success);
        Assert.False(result.Changed);
        Assert.Null(result.ErrorCode);
        Assert.False(result.ShouldStopScan);

        TrackedTorrent updated = repository.Torrents[0];
        Assert.Equal(TorrentCheckOutcome.Unchanged, updated.LastCheckOutcome);
        Assert.Null(updated.LastError);
        Assert.Null(updated.LastChangedAtUtc);
        Assert.Equal(Now.UtcDateTime, updated.LastCheckedAtUtc);
        Assert.Equal(Now.UtcDateTime, updated.UpdatedAtUtc);
        Assert.Equal(metadata.InfoHashV1, updated.InfoHashV1);
        Assert.Equal(metadata.Name, updated.Title);
        Assert.Equal(metadata.SizeBytes, updated.TorrentSizeBytes);
        Assert.Equal(1, storage.SaveCount);
    }

    [Fact]
    public async Task CheckAsync_NewInfoHash_RecordsChangedAndUpdatesMetadata()
    {
        (byte[] payload, byte[] info) = TorrentFixtures.SingleFile("Новое название", length: 8_192);
        TorrentMetadata metadata = TorrentMetadataParser.Parse(payload);

        TrackedTorrent torrent = Torrent();
        torrent.Title = "Старое название";
        torrent.InfoHashV1 = new string('b', 40);
        torrent.LastChangedAtUtc = null;

        var repository = new InMemoryTrackedTorrentRepository(torrent);
        var storage = new RecordingTorrentFileStorage(Now);
        TorrentScanService service = CreateService(
            repository,
            storage,
            new StubRutrackerClient(_ => Task.FromResult(payload)));

        TorrentCheckResult result = await service.CheckAsync(torrent.Id, CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.Changed);
        Assert.Equal(TorrentCheckOutcome.Changed, result.Torrent.LastCheckOutcome);
        Assert.Equal(Now.UtcDateTime, result.Torrent.LastChangedAtUtc);

        TrackedTorrent updated = repository.Torrents[0];
        Assert.Equal(metadata.InfoHashV1, updated.InfoHashV1);
        Assert.Equal("Новое название", updated.Title);
        Assert.Equal(8_192, updated.TorrentSizeBytes);
        Assert.Equal(1, updated.FileCount);
        Assert.Equal(Now.UtcDateTime, updated.LastChangedAtUtc);
        Assert.Null(updated.LastError);
        Assert.Equal(1, storage.SaveCount);
        Assert.Equal(payload, storage.Files[torrent.TopicId]);
    }

    [Fact]
    public async Task CheckAsync_UnchangedAfterPreviousChange_KeepsLastChangedDate()
    {
        DateTime previousChange = Now.UtcDateTime.AddDays(-2);
        (byte[] payload, byte[] info) = TorrentFixtures.SingleFile();

        TrackedTorrent torrent = Torrent();
        torrent.InfoHashV1 = TorrentFixtures.ExpectedInfoHash(info);
        torrent.LastChangedAtUtc = previousChange;
        torrent.LastCheckOutcome = TorrentCheckOutcome.Changed;

        var repository = new InMemoryTrackedTorrentRepository(torrent);
        TorrentScanService service = CreateService(
            repository,
            new RecordingTorrentFileStorage(Now),
            new StubRutrackerClient(_ => Task.FromResult(payload)));

        await service.CheckAsync(torrent.Id, CancellationToken.None);

        Assert.Equal(previousChange, repository.Torrents[0].LastChangedAtUtc);
        Assert.Equal(TorrentCheckOutcome.Unchanged, repository.Torrents[0].LastCheckOutcome);
    }

    [Fact]
    public async Task CheckAsync_NetworkError_RecordsErrorAndDoesNotTouchHash()
    {
        TrackedTorrent torrent = Torrent();
        torrent.InfoHashV1 = new string('c', 40);
        torrent.LastChangedAtUtc = Now.UtcDateTime.AddDays(-1);

        var repository = new InMemoryTrackedTorrentRepository(torrent);
        var storage = new RecordingTorrentFileStorage(Now);
        TorrentScanService service = CreateService(
            repository,
            storage,
            new StubRutrackerClient(_ => throw new RutrackerException(
                RutrackerFailureKind.Network,
                "Рутрекер не ответил.")));

        TorrentCheckResult result = await service.CheckAsync(torrent.Id, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.RutrackerUnavailable, result.ErrorCode);
        Assert.False(result.ShouldStopScan);

        TrackedTorrent updated = repository.Torrents[0];
        Assert.Equal(TorrentCheckOutcome.Error, updated.LastCheckOutcome);
        Assert.Equal("Рутрекер не ответил.", updated.LastError);
        Assert.Equal(new string('c', 40), updated.InfoHashV1);
        Assert.Equal(Now.UtcDateTime.AddDays(-1), updated.LastChangedAtUtc);
        Assert.Equal(Now.UtcDateTime, updated.LastCheckedAtUtc);
        Assert.Equal(0, storage.SaveCount);
    }

    [Theory]
    [InlineData(RutrackerFailureKind.CookieInvalid, ErrorCodes.RutrackerCookieInvalid)]
    [InlineData(RutrackerFailureKind.InvalidPayload, ErrorCodes.RutrackerInvalidPayload)]
    public async Task CheckAsync_CookieOrPayloadFailure_StopsScan(RutrackerFailureKind kind, string expectedCode)
    {
        TrackedTorrent torrent = Torrent();
        TorrentScanService service = CreateService(
            new InMemoryTrackedTorrentRepository(torrent),
            new RecordingTorrentFileStorage(Now),
            new StubRutrackerClient(_ => throw new RutrackerException(kind, "Не .torrent.")));

        TorrentCheckResult result = await service.CheckAsync(torrent.Id, CancellationToken.None);

        Assert.Equal(expectedCode, result.ErrorCode);
        Assert.True(result.ShouldStopScan);
    }

    [Fact]
    public async Task CheckAsync_NotFound_RecordsErrorButContinuesScan()
    {
        TrackedTorrent torrent = Torrent();
        TorrentScanService service = CreateService(
            new InMemoryTrackedTorrentRepository(torrent),
            new RecordingTorrentFileStorage(Now),
            new StubRutrackerClient(_ => throw new RutrackerException(
                RutrackerFailureKind.NotFound,
                "Топик удалён.")));

        TorrentCheckResult result = await service.CheckAsync(torrent.Id, CancellationToken.None);

        Assert.Equal(ErrorCodes.TopicNotFound, result.ErrorCode);
        Assert.False(result.ShouldStopScan);
    }

    [Fact]
    public async Task CheckAsync_BrokenBencode_RecordsInvalidPayloadAndStopsScan()
    {
        TrackedTorrent torrent = Torrent();
        var storage = new RecordingTorrentFileStorage(Now);
        TorrentScanService service = CreateService(
            new InMemoryTrackedTorrentRepository(torrent),
            storage,
            new StubRutrackerClient(_ => Task.FromResult(Encoding.ASCII.GetBytes("<html>капча</html>"))));

        TorrentCheckResult result = await service.CheckAsync(torrent.Id, CancellationToken.None);

        Assert.Equal(ErrorCodes.RutrackerInvalidPayload, result.ErrorCode);
        Assert.True(result.ShouldStopScan);
        Assert.Equal(TorrentCheckOutcome.Error, result.Torrent.LastCheckOutcome);
        Assert.Equal(0, storage.SaveCount);
    }

    [Fact]
    public async Task CheckAsync_NoCookie_RecordsErrorWithoutNetworkCall()
    {
        TrackedTorrent torrent = Torrent();
        var rutracker = new StubRutrackerClient(_ => throw new InvalidOperationException("не должен вызываться"));
        TorrentScanService service = CreateService(
            new InMemoryTrackedTorrentRepository(torrent),
            new RecordingTorrentFileStorage(Now),
            rutracker,
            SettingsWithCookie(string.Empty));

        TorrentCheckResult result = await service.CheckAsync(torrent.Id, CancellationToken.None);

        Assert.Equal(ErrorCodes.RutrackerCookieInvalid, result.ErrorCode);
        Assert.True(result.ShouldStopScan);
        Assert.Contains("не задана", result.Torrent.LastError);
        Assert.Empty(rutracker.Requests);
    }

    [Fact]
    public async Task CheckAsync_StorageFailure_RecordsErrorAndKeepsHash()
    {
        (byte[] payload, _) = TorrentFixtures.SingleFile();
        string oldHash = new('d', 40);

        TrackedTorrent torrent = Torrent();
        torrent.InfoHashV1 = oldHash;

        var repository = new InMemoryTrackedTorrentRepository(torrent);
        var storage = new RecordingTorrentFileStorage(Now) { ThrowOnSave = true };
        TorrentScanService service = CreateService(
            repository,
            storage,
            new StubRutrackerClient(_ => Task.FromResult(payload)));

        TorrentCheckResult result = await service.CheckAsync(torrent.Id, CancellationToken.None);

        Assert.Equal(ErrorCodes.StorageFailure, result.ErrorCode);
        Assert.False(result.ShouldStopScan);
        Assert.Equal(oldHash, repository.Torrents[0].InfoHashV1);
        Assert.Equal(TorrentCheckOutcome.Error, repository.Torrents[0].LastCheckOutcome);
    }

    [Fact]
    public async Task CheckAsync_UnknownId_ThrowsTorrentNotFound()
    {
        TorrentScanService service = CreateService(
            new InMemoryTrackedTorrentRepository(),
            new RecordingTorrentFileStorage(Now),
            new StubRutrackerClient(_ => Task.FromResult(Array.Empty<byte>())));

        AppException exception = await Assert.ThrowsAsync<AppException>(
            () => service.CheckAsync(404, CancellationToken.None));

        Assert.Equal(ErrorCodes.TorrentNotFound, exception.Code);
    }

    private static TorrentScanService CreateService(
        ITrackedTorrentRepository repository,
        RecordingTorrentFileStorage storage,
        IRutrackerClient rutracker,
        ISettingsProvider? settingsProvider = null) =>
        new(repository, rutracker, storage, settingsProvider ?? SettingsWithCookie(), new FixedTimeProvider(Now));

    private static SettingsProvider SettingsWithCookie(string cookie = "bb_session=secret")
    {
        var provider = new SettingsProvider();
        provider.Apply(AppSettingsSnapshot.FromEntity(new AppSetting
        {
            RutrackerCookie = cookie,
            RequestTimeoutSeconds = 30,
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
        InfoHashV1 = new string('a', 40),
        TorrentSizeBytes = 1,
        FileCount = 1,
        TorrentFilePath = "/data/torrents/555.torrent",
        TorrentFileSha256 = new string('0', 64),
        TorrentFileDownloadedAtUtc = Now.UtcDateTime.AddDays(-1),
        LastCheckOutcome = TorrentCheckOutcome.Never,
        IsEnabled = true,
        CreatedAtUtc = Now.UtcDateTime.AddDays(-1),
        UpdatedAtUtc = Now.UtcDateTime.AddDays(-1),
    };
}
