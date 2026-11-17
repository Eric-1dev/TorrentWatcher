using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using TorrentWatcher.Application.Abstractions;
using TorrentWatcher.Application.Dtos;
using TorrentWatcher.Application.Services;
using TorrentWatcher.Application.Settings;
using TorrentWatcher.Application.State;
using TorrentWatcher.Domain;
using TorrentWatcher.Domain.Entities;
using TorrentWatcher.Domain.Parsing;
using TorrentWatcher.UnitTests.Domain.Parsing;
using TorrentWatcher.UnitTests.Support;

namespace TorrentWatcher.UnitTests.Application.Scanning;

public sealed class ScanSchedulerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 19, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task StartAsync_RunsScanWhenIntervalElapses()
    {
        await using var harness = new Harness(SinceLastScan: TimeSpan.FromMinutes(59));

        await harness.Scheduler.StartAsync(CancellationToken.None);

        harness.Time.Advance(TimeSpan.FromSeconds(59));
        await Task.Delay(30);
        Assert.Empty(harness.Rutracker.Requests);

        harness.Time.Advance(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => harness.Rutracker.Requests.Count > 0);

        Assert.NotEmpty(harness.Rutracker.Requests);
        Assert.Equal(ScanRunState.Completed, harness.StatusStore.Current?.Status);
    }

    [Fact]
    public async Task ChangedInterval_RecalculatesNextRunWithoutRestart()
    {
        // Последний скан только что, интервал 60 минут; меняем на 10 — скан должен прийти раньше.
        await using var harness = new Harness(SinceLastScan: TimeSpan.Zero);
        await harness.Scheduler.StartAsync(CancellationToken.None);
        await Task.Delay(50);

        await harness.UpdateSettingsAsync(intervalMinutes: 10);
        Assert.Equal(10, harness.Settings.Current.ScanIntervalMinutes);
        await Task.Delay(50);

        harness.Time.Advance(TimeSpan.FromMinutes(9));
        await Task.Delay(30);
        Assert.Empty(harness.Rutracker.Requests);

        harness.Time.Advance(TimeSpan.FromMinutes(1));
        await WaitUntilAsync(
            () => harness.Rutracker.Requests.Count > 0,
            () => string.Join(Environment.NewLine, harness.Logger.Entries));

        Assert.NotEmpty(harness.Rutracker.Requests);
    }

    [Fact]
    public async Task AutoScanDisabled_DoesNotRunUntilEnabled()
    {
        await using var harness = new Harness(
            SinceLastScan: TimeSpan.FromMinutes(5),
            autoScanEnabled: false);
        await harness.Scheduler.StartAsync(CancellationToken.None);

        harness.Time.Advance(TimeSpan.FromHours(2));
        await Task.Delay(50);
        Assert.Empty(harness.Rutracker.Requests);

        await harness.UpdateSettingsAsync(intervalMinutes: 60, autoScanEnabled: true);
        await Task.Delay(50);

        harness.Time.Advance(TimeSpan.FromMinutes(60));
        await WaitUntilAsync(
            () => harness.Rutracker.Requests.Count > 0,
            () => string.Join(Environment.NewLine, harness.Logger.Entries));

        Assert.NotEmpty(harness.Rutracker.Requests);
    }

    [Fact]
    public async Task StopAsync_StopsLoopWithoutScan()
    {
        await using var harness = new Harness(SinceLastScan: TimeSpan.Zero);
        await harness.Scheduler.StartAsync(CancellationToken.None);

        await harness.Scheduler.StopAsync(CancellationToken.None);
        harness.Time.Advance(TimeSpan.FromHours(5));
        await Task.Delay(50);

        Assert.Empty(harness.Rutracker.Requests);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, Func<string>? diagnostics = null)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);

        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                string details = diagnostics?.Invoke() ?? string.Empty;
                throw new TimeoutException(
                    $"Ожидаемое событие не наступило за 5 секунд. {details}");
            }

            await Task.Delay(10);
        }
    }

    private sealed class TestLogger : ILogger<ScanScheduler>
    {
        public List<string> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add($"{logLevel}: {formatter(state, exception)}");
        }
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;

        public Harness(TimeSpan SinceLastScan, bool autoScanEnabled = true)
        {
            Time = new FakeTimeProvider(Now);

            (byte[] payload, byte[] info) = TorrentFixtures.SingleFile();

            Torrents = new InMemoryTrackedTorrentRepository(new TrackedTorrent
            {
                Id = 1,
                TopicId = 777,
                TopicUrl = RutrackerUrls.Topic(777),
                Title = "Торрент",
                InfoHashV1 = TorrentFixtures.ExpectedInfoHash(info),
                TorrentSizeBytes = 1024,
                FileCount = 1,
                TorrentFilePath = "/data/torrents/777.torrent",
                TorrentFileSha256 = new string('0', 64),
                TorrentFileDownloadedAtUtc = Now.UtcDateTime.AddDays(-1),
                LastCheckOutcome = TorrentCheckOutcome.Unchanged,
                IsEnabled = true,
                CreatedAtUtc = Now.UtcDateTime.AddDays(-1),
                UpdatedAtUtc = Now.UtcDateTime.AddDays(-1),
            });

            SettingsRepository = new InMemoryAppSettingsRepository(new AppSetting
            {
                RutrackerCookie = "bb_session=secret",
                ScanIntervalMinutes = 60,
                AutoScanEnabled = autoScanEnabled,
                RequestDelayMs = 0,
                RequestTimeoutSeconds = 30,
                LastScanAtUtc = (Now - SinceLastScan).UtcDateTime,
                UpdatedAtUtc = Now.UtcDateTime,
            });

            Settings = new SettingsProvider();
            Settings.Apply(AppSettingsSnapshot.FromEntity(SettingsRepository.Setting!));

            Rutracker = new StubRutrackerClient(_ => Task.FromResult(payload));
            StatusStore = new ScanStatusStore();

            var services = new ServiceCollection();
            services.AddSingleton<ITrackedTorrentRepository>(Torrents);
            services.AddSingleton<IAppSettingsRepository>(SettingsRepository);
            services.AddSingleton<IRutrackerClient>(Rutracker);
            services.AddSingleton<ITorrentFileStorage>(new RecordingTorrentFileStorage(Now));
            services.AddSingleton<ISettingsProvider>(Settings);
            services.AddSingleton<TimeProvider>(Time);
            services.AddScoped<TorrentScanService>();
            services.AddScoped<SettingsService>();
            services.AddSingleton(StatusStore);
            services.AddSingleton<ScanCoordinator>();

            _provider = services.BuildServiceProvider();
            Coordinator = _provider.GetRequiredService<ScanCoordinator>();
            Scheduler = new ScanScheduler(
                Coordinator,
                Settings,
                Time,
                Logger);
        }

        public FakeTimeProvider Time { get; }

        public TestLogger Logger { get; } = new();

        public InMemoryTrackedTorrentRepository Torrents { get; }

        public InMemoryAppSettingsRepository SettingsRepository { get; }

        public SettingsProvider Settings { get; }

        public StubRutrackerClient Rutracker { get; }

        public ScanStatusStore StatusStore { get; }

        public ScanCoordinator Coordinator { get; }

        public ScanScheduler Scheduler { get; }

        public async Task UpdateSettingsAsync(int intervalMinutes, bool autoScanEnabled = true)        {
            using IServiceScope scope = _provider.CreateScope();
            var settingsService = scope.ServiceProvider.GetRequiredService<SettingsService>();

            await settingsService.UpdateAsync(
                new SettingsUpdate(
                    RutrackerCookie: null,
                    ScanIntervalMinutes: intervalMinutes,
                    AutoScanEnabled: autoScanEnabled,
                    RequestDelayMs: 0,
                    RequestTimeoutSeconds: 30),
                CancellationToken.None);
        }

        public async ValueTask DisposeAsync()
        {
            await Scheduler.StopAsync(CancellationToken.None);
            Scheduler.Dispose();
            _provider.Dispose();
        }
    }
}
