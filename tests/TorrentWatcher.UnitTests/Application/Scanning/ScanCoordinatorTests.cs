using Microsoft.Extensions.DependencyInjection;
using TorrentWatcher.Application.Abstractions;
using TorrentWatcher.Application.Dtos;
using TorrentWatcher.Application.Exceptions;
using TorrentWatcher.Application.Rutracker;
using TorrentWatcher.Application.Services;
using TorrentWatcher.Application.Settings;
using TorrentWatcher.Application.State;
using TorrentWatcher.Domain;
using TorrentWatcher.Domain.Entities;
using TorrentWatcher.Domain.Parsing;
using TorrentWatcher.UnitTests.Domain.Parsing;
using TorrentWatcher.UnitTests.Support;

namespace TorrentWatcher.UnitTests.Application.Scanning;

public sealed class ScanCoordinatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunAsync_ProcessesEnabledTorrentsAndCountsChanges()
    {
        (byte[] payload, byte[] info) = TorrentFixtures.SingleFile();
        string currentHash = TorrentFixtures.ExpectedInfoHash(info);

        using var harness = new Harness(
            _ => Task.FromResult(payload),
            Torrent(1, 101, currentHash),
            Torrent(2, 102, new string('b', 40)));

        ScanStatus status = await harness.Coordinator.RunAsync(ScanTrigger.Manual, CancellationToken.None);

        Assert.Equal(ScanRunState.Completed, status.Status);
        Assert.Equal(ScanTrigger.Manual, status.Trigger);
        Assert.Equal(2, status.TorrentsTotal);
        Assert.Equal(1, status.ChangedCount);
        Assert.Equal(0, status.FailedCount);
        Assert.Null(status.Message);
        Assert.NotNull(status.FinishedAtUtc);
        Assert.False(status.IsRunning);
        Assert.Equal(2, harness.Rutracker.Requests.Count);
        Assert.Equal(status.RunId, harness.StatusStore.Current?.RunId);
        Assert.Equal(Now.UtcDateTime, harness.SettingsRepository.Setting?.LastScanAtUtc);
    }

    [Fact]
    public async Task RunAsync_SkipsDisabledTorrents()
    {
        (byte[] payload, _) = TorrentFixtures.SingleFile();

        using var harness = new Harness(
            _ => Task.FromResult(payload),
            Torrent(1, 101, new string('a', 40)),
            Torrent(2, 102, new string('b', 40), isEnabled: false),
            Torrent(3, 103, new string('c', 40)));

        ScanStatus status = await harness.Coordinator.RunAsync(ScanTrigger.Auto, CancellationToken.None);

        Assert.Equal(2, status.TorrentsTotal);
        Assert.Equal(2, harness.Rutracker.Requests.Count);
        Assert.DoesNotContain(harness.Rutracker.Requests, request => request.TopicId == 102);
    }

    [Fact]
    public async Task RunAsync_NetworkErrors_DoNotStopRun()
    {
        using var harness = new Harness(
            _ => throw new RutrackerException(RutrackerFailureKind.Network, "Нет связи."),
            Torrent(1, 101, new string('a', 40)),
            Torrent(2, 102, new string('b', 40)),
            Torrent(3, 103, new string('c', 40)));

        ScanStatus status = await harness.Coordinator.RunAsync(ScanTrigger.Auto, CancellationToken.None);

        Assert.Equal(ScanRunState.Completed, status.Status);
        Assert.Equal(3, status.FailedCount);
        Assert.Equal(0, status.ChangedCount);
        Assert.Equal(3, harness.Rutracker.Requests.Count);
        Assert.Null(harness.SettingsRepository.Setting?.CookieLastError);
    }

    [Fact]
    public async Task RunAsync_TwoConsecutiveCookieFailures_StopsRunAndInvalidatesCookie()
    {
        using var harness = new Harness(
            _ => throw new RutrackerException(RutrackerFailureKind.CookieInvalid, "Кука отклонена."),
            Torrent(1, 101, new string('a', 40)),
            Torrent(2, 102, new string('b', 40)),
            Torrent(3, 103, new string('c', 40)));

        ScanStatus status = await harness.Coordinator.RunAsync(ScanTrigger.Auto, CancellationToken.None);

        Assert.Equal(ScanRunState.Failed, status.Status);
        Assert.Equal(3, status.TorrentsTotal);
        Assert.Equal(2, status.FailedCount);
        Assert.Contains("не принимает куку", status.Message);
        Assert.Equal(2, harness.Rutracker.Requests.Count);

        Assert.Equal("Кука отклонена.", harness.SettingsRepository.Setting?.CookieLastError);
        Assert.Null(harness.SettingsRepository.Setting?.CookieVerifiedAtUtc);
        Assert.Equal("Кука отклонена.", harness.Settings.Current.CookieLastError);
    }

    [Fact]
    public async Task RunAsync_FailureThenSuccess_ResetsConsecutiveFailureCounter()
    {
        (byte[] payload, _) = TorrentFixtures.SingleFile();
        int calls = 0;

        using var harness = new Harness(
            _ => Interlocked.Increment(ref calls) == 2
                ? Task.FromResult(payload)
                : throw new RutrackerException(RutrackerFailureKind.CookieInvalid, "Кука отклонена."),
            Torrent(1, 101, new string('a', 40)),
            Torrent(2, 102, new string('b', 40)),
            Torrent(3, 103, new string('c', 40)));

        ScanStatus status = await harness.Coordinator.RunAsync(ScanTrigger.Auto, CancellationToken.None);

        Assert.Equal(ScanRunState.Completed, status.Status);
        Assert.Equal(3, harness.Rutracker.Requests.Count);
        Assert.Equal(2, status.FailedCount);
        Assert.Null(harness.SettingsRepository.Setting?.CookieLastError);
    }

    [Fact]
    public async Task RunAsync_CanceledDuringScan_FinishesAsFailed()
    {
        (byte[] payload, _) = TorrentFixtures.SingleFile();
        var firstChecked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var harness = new Harness(
            _ =>
            {
                firstChecked.TrySetResult();
                return Task.FromResult(payload);
            },
            Torrent(1, 101, new string('a', 40)),
            Torrent(2, 102, new string('b', 40)));
        harness.Settings.Apply(AppSettingsSnapshot.FromEntity(new AppSetting
        {
            RutrackerCookie = "bb_session=secret",
            RequestDelayMs = 5_000,
            RequestTimeoutSeconds = 30,
            UpdatedAtUtc = Now.UtcDateTime,
        }));

        using var cts = new CancellationTokenSource();
        Task<ScanStatus> run = harness.Coordinator.RunAsync(ScanTrigger.Manual, cts.Token);
        await firstChecked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cts.CancelAsync();

        ScanStatus status = await run.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(ScanRunState.Failed, status.Status);
        Assert.Equal("Скан отменён.", status.Message);
        Assert.Single(harness.Rutracker.Requests);
    }

    [Fact]
    public async Task StartBackground_WhenRunInProgress_ReturnsExistingRun()
    {
        (byte[] payload, _) = TorrentFixtures.SingleFile();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var harness = new Harness(
            async _ =>
            {
                entered.TrySetResult();
                await release.Task;
                return payload;
            },
            Torrent(1, 101, new string('a', 40)));

        ScanStartResult first = harness.Coordinator.StartBackground(ScanTrigger.Manual);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        ScanStartResult second = harness.Coordinator.StartBackground(ScanTrigger.Auto);

        Assert.True(first.Started);
        Assert.False(second.Started);
        Assert.Equal(first.RunId, second.RunId);
        Assert.True(harness.Coordinator.Current?.IsRunning);

        release.SetResult();
        ScanStatus status = await WaitUntilFinishedAsync(harness.Coordinator);

        Assert.Equal(ScanRunState.Completed, status.Status);
        Assert.Equal(1, status.TorrentsTotal);
        Assert.Single(harness.Rutracker.Requests);
    }

    private static async Task<ScanStatus> WaitUntilFinishedAsync(ScanCoordinator coordinator)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);

        while (DateTime.UtcNow < deadline)
        {
            ScanStatus? status = coordinator.Current;
            if (status is not null && !status.IsRunning)
            {
                return status;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("Скан не завершился за 5 секунд.");
    }

    private static TrackedTorrent Torrent(
        int id,
        int topicId,
        string infoHash,
        bool isEnabled = true) => new()
        {
            Id = id,
            TopicId = topicId,
            TopicUrl = RutrackerUrls.Topic(topicId),
            Title = $"Торрент {topicId}",
            InfoHashV1 = infoHash,
            TorrentSizeBytes = 1,
            FileCount = 1,
            TorrentFilePath = $"/data/torrents/{topicId}.torrent",
            TorrentFileSha256 = new string('0', 64),
            TorrentFileDownloadedAtUtc = Now.UtcDateTime.AddDays(-1),
            LastCheckOutcome = TorrentCheckOutcome.Never,
            IsEnabled = isEnabled,
            CreatedAtUtc = Now.UtcDateTime.AddDays(-1),
            UpdatedAtUtc = Now.UtcDateTime.AddDays(-1),
        };

    private sealed class Harness : IDisposable
    {
        private readonly ServiceProvider _provider;

        public Harness(Func<RutrackerDownloadRequest, Task<byte[]>> handler, params TrackedTorrent[] torrents)
        {
            Torrents = new InMemoryTrackedTorrentRepository(torrents);
            SettingsRepository = new InMemoryAppSettingsRepository(new AppSetting
            {
                RutrackerCookie = "bb_session=secret",
                RequestDelayMs = 0,
                RequestTimeoutSeconds = 30,
                UpdatedAtUtc = Now.UtcDateTime,
            });

            Settings = new SettingsProvider();
            Settings.Apply(AppSettingsSnapshot.FromEntity(SettingsRepository.Setting!));

            Rutracker = new StubRutrackerClient(handler);
            StatusStore = new ScanStatusStore();

            var services = new ServiceCollection();
            services.AddSingleton<ITrackedTorrentRepository>(Torrents);
            services.AddSingleton<IAppSettingsRepository>(SettingsRepository);
            services.AddSingleton<IRutrackerClient>(Rutracker);
            services.AddSingleton<ITorrentFileStorage>(new RecordingTorrentFileStorage(Now));
            services.AddSingleton<ISettingsProvider>(Settings);
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
            services.AddScoped<TorrentScanService>();
            services.AddScoped<SettingsService>();
            services.AddSingleton(StatusStore);
            services.AddSingleton<ScanCoordinator>();

            _provider = services.BuildServiceProvider();
            Coordinator = _provider.GetRequiredService<ScanCoordinator>();
        }

        public InMemoryTrackedTorrentRepository Torrents { get; }

        public InMemoryAppSettingsRepository SettingsRepository { get; }

        public SettingsProvider Settings { get; }

        public StubRutrackerClient Rutracker { get; }

        public ScanStatusStore StatusStore { get; }

        public ScanCoordinator Coordinator { get; }

        public void Dispose() => _provider.Dispose();
    }
}
