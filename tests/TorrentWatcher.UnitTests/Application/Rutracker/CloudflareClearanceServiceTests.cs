using Microsoft.Extensions.Logging.Abstractions;
using TorrentWatcher.Application.Abstractions;
using TorrentWatcher.Application.Dtos;
using TorrentWatcher.Application.Exceptions;
using TorrentWatcher.Application.Rutracker;
using TorrentWatcher.Application.Services;
using TorrentWatcher.Application.Settings;
using TorrentWatcher.Domain.Entities;
using TorrentWatcher.UnitTests.Support;

namespace TorrentWatcher.UnitTests.Application.Rutracker;

public sealed class CloudflareClearanceServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SolveAsync_PersistsClearanceAndUpdatesSnapshot()
    {
        AppSetting setting = NewSetting();
        var repository = new InMemoryAppSettingsRepository(setting);
        var provider = new SettingsProvider();
        provider.Apply(AppSettingsSnapshot.FromEntity(setting));

        var solver = new FakeSolver(new CloudflareClearance(
            "cf_clearance=ready",
            "UA-real",
            Now.UtcDateTime,
            Now.UtcDateTime.AddYears(1)));

        CloudflareClearanceService service = CreateService(repository, provider, solver);

        CloudflareClearance clearance = await service.SolveAsync(77, CancellationToken.None);

        Assert.Equal("cf_clearance=ready", clearance.Cookie);
        Assert.Equal(1, repository.SaveCount);
        Assert.Equal("cf_clearance=ready", setting.CloudflareClearanceCookie);
        Assert.Equal("UA-real", setting.CloudflareUserAgent);
        Assert.Equal(Now.UtcDateTime.AddYears(1), setting.CloudflareClearanceExpiresAtUtc);
        Assert.Null(setting.CloudflareLastError);

        Assert.NotNull(provider.Current.CloudflareClearanceCookie);
        Assert.Equal("UA-real", provider.Current.CloudflareUserAgent);
        Assert.NotNull(service.Current);
    }

    [Fact]
    public async Task Current_ExpiredClearance_ReturnsNull()
    {
        AppSetting setting = NewSetting();
        setting.CloudflareClearanceCookie = "cf_clearance=old";
        setting.CloudflareUserAgent = "UA-old";
        setting.CloudflareClearanceObtainedAtUtc = Now.UtcDateTime.AddDays(-30);
        setting.CloudflareClearanceExpiresAtUtc = Now.UtcDateTime.AddMinutes(-1);

        var provider = new SettingsProvider();
        provider.Apply(AppSettingsSnapshot.FromEntity(setting));

        CloudflareClearanceService service = CreateService(
            new InMemoryAppSettingsRepository(setting),
            provider,
            new FakeSolver());

        Assert.Null(service.Current);
    }

    [Fact]
    public async Task ReportRejectedAsync_ClearsClearanceAndStoresError()
    {
        AppSetting setting = NewSetting();
        setting.CloudflareClearanceCookie = "cf_clearance=old";
        setting.CloudflareClearanceExpiresAtUtc = Now.UtcDateTime.AddDays(1);

        var repository = new InMemoryAppSettingsRepository(setting);
        var provider = new SettingsProvider();
        provider.Apply(AppSettingsSnapshot.FromEntity(setting));

        CloudflareClearanceService service = CreateService(
            repository,
            provider,
            new FakeSolver());

        await service.ReportRejectedAsync("Повторный челлендж.", Now.UtcDateTime, CancellationToken.None);

        Assert.Null(setting.CloudflareClearanceCookie);
        Assert.Null(setting.CloudflareClearanceExpiresAtUtc);
        Assert.Equal("Повторный челлендж.", setting.CloudflareLastError);
        Assert.Equal("Повторный челлендж.", provider.Current.CloudflareLastError);
    }

    [Fact]
    public async Task SolveForUiAsync_WithoutTorrents_UsesTopicOne()
    {
        var setting = NewSetting();
        var solver = new FakeSolver(new CloudflareClearance(
            "cf_clearance=ready",
            "UA-real",
            Now.UtcDateTime,
            null));

        CloudflareClearanceService service = CreateService(
            new InMemoryAppSettingsRepository(setting),
            new SettingsProvider(),
            solver);

        CloudflareSolveResult result = await service.SolveForUiAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, solver.LastTopicId);
    }

    [Fact]
    public async Task SolveForUiAsync_SolverFails_ReturnsFailureWithoutThrowing()
    {
        var solver = new FakeSolver(exception: new RutrackerException(
            RutrackerFailureKind.Challenge,
            "Браузер не запустился."));

        CloudflareClearanceService service = CreateService(
            new InMemoryAppSettingsRepository(NewSetting()),
            new SettingsProvider(),
            solver);

        CloudflareSolveResult result = await service.SolveForUiAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Браузер не запустился.", result.Message);
    }

    private static CloudflareClearanceService CreateService(
        IAppSettingsRepository repository,
        SettingsProvider provider,
        ICloudflareChallengeSolver solver) =>
        new(
            repository,
            new InMemoryTrackedTorrentRepository(),
            solver,
            provider,
            new SettingsWriteGate(),
            new FixedTimeProvider(Now),
            NullLogger<CloudflareClearanceService>.Instance);

    private static AppSetting NewSetting() => new()
    {
        Id = AppSetting.DefaultId,
        UpdatedAtUtc = Now.UtcDateTime,
    };

    private sealed class FakeSolver : ICloudflareChallengeSolver
    {
        private readonly CloudflareClearance? _clearance;
        private readonly Exception? _exception;

        public FakeSolver(CloudflareClearance? clearance = null, Exception? exception = null)
        {
            _clearance = clearance;
            _exception = exception;
        }

        public int? LastTopicId { get; private set; }

        public Task<CloudflareClearance> SolveAsync(int topicId, CancellationToken cancellationToken)
        {
            LastTopicId = topicId;

            if (_exception is not null)
            {
                throw _exception;
            }

            return Task.FromResult(_clearance!);
        }
    }
}
