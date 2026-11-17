using TorrentWatcher.Application.Abstractions;
using TorrentWatcher.Application.Dtos;
using TorrentWatcher.Application.Exceptions;
using TorrentWatcher.Application.Rutracker;
using TorrentWatcher.Application.Services;
using TorrentWatcher.Application.Settings;
using TorrentWatcher.Domain.Entities;
using TorrentWatcher.UnitTests.Support;

namespace TorrentWatcher.UnitTests.Application.Settings;

public sealed class SettingsServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 15, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1441)]
    public async Task UpdateAsync_IntervalOutsideRange_ThrowsAndDoesNotSave(int interval)
    {
        var repository = new InMemoryAppSettingsRepository(NewSetting());
        SettingsProvider provider = new();
        SettingsService service = CreateService(repository, provider);

        AppException exception = await Assert.ThrowsAsync<AppException>(
            () => service.UpdateAsync(ValidUpdate() with { ScanIntervalMinutes = interval }, CancellationToken.None));

        Assert.Equal(ErrorCodes.SettingsInvalid, exception.Code);
        Assert.Equal(0, repository.SaveCount);
        Assert.Equal(AppSettingsSnapshotDefaultInterval(), provider.Current.ScanIntervalMinutes);
    }

    [Theory]
    [InlineData(-1, 30)]
    [InlineData(1000, 0)]
    [InlineData(1000, -5)]
    public async Task UpdateAsync_InvalidDelayOrTimeout_Throws(int delayMs, int timeoutSeconds)
    {
        var repository = new InMemoryAppSettingsRepository(NewSetting());
        SettingsService service = CreateService(repository, new SettingsProvider());

        await Assert.ThrowsAsync<AppException>(
            () => service.UpdateAsync(
                ValidUpdate() with { RequestDelayMs = delayMs, RequestTimeoutSeconds = timeoutSeconds },
                CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_Valid_AppliesSnapshotAndRaisesChanged()
    {
        var repository = new InMemoryAppSettingsRepository(NewSetting());
        SettingsProvider provider = new();
        int changedCount = 0;
        provider.Changed += _ => changedCount++;

        SettingsService service = CreateService(repository, provider);
        SettingsView view = await service.UpdateAsync(
            new SettingsUpdate(
                RutrackerCookie: "bb_session=secret1234",
                ScanIntervalMinutes: 15,
                AutoScanEnabled: false,
                RequestDelayMs: 250,
                RequestTimeoutSeconds: 10),
            CancellationToken.None);

        Assert.Equal(1, changedCount);
        Assert.Equal(15, provider.Current.ScanIntervalMinutes);
        Assert.False(provider.Current.AutoScanEnabled);
        Assert.Equal(250, provider.Current.RequestDelayMs);
        Assert.Equal(10, provider.Current.RequestTimeoutSeconds);
        Assert.Equal("bb_session=secret1234", provider.Current.RutrackerCookie);
        Assert.Equal(Now.UtcDateTime, provider.Current.UpdatedAtUtc);
        Assert.True(view.HasCookie);
    }

    [Fact]
    public async Task UpdateAsync_NewCookie_ResetsVerificationState()
    {
        AppSetting setting = NewSetting();
        setting.RutrackerCookie = "bb_session=old";
        setting.CookieVerifiedAtUtc = Now.UtcDateTime.AddDays(-1);
        setting.CookieLastError = "старая ошибка";

        var repository = new InMemoryAppSettingsRepository(setting);
        SettingsProvider provider = new();
        SettingsService service = CreateService(repository, provider);

        await service.UpdateAsync(
            ValidUpdate() with { RutrackerCookie = "bb_session=new" },
            CancellationToken.None);

        Assert.Equal("bb_session=new", setting.RutrackerCookie);
        Assert.Null(setting.CookieVerifiedAtUtc);
        Assert.Null(setting.CookieLastError);
        Assert.Null(provider.Current.CookieVerifiedAtUtc);
    }

    [Fact]
    public async Task UpdateAsync_SameCookie_KeepsVerificationState()
    {
        AppSetting setting = NewSetting();
        setting.RutrackerCookie = "bb_session=old";
        setting.CookieVerifiedAtUtc = Now.UtcDateTime.AddDays(-1);

        var repository = new InMemoryAppSettingsRepository(setting);
        SettingsService service = CreateService(repository, new SettingsProvider());

        await service.UpdateAsync(
            ValidUpdate() with { RutrackerCookie = "bb_session=old" },
            CancellationToken.None);

        Assert.Equal(Now.UtcDateTime.AddDays(-1), setting.CookieVerifiedAtUtc);
    }

    [Fact]
    public async Task UpdateAsync_NullCookie_KeepsStoredCookie()
    {
        AppSetting setting = NewSetting();
        setting.RutrackerCookie = "bb_session=old";
        var repository = new InMemoryAppSettingsRepository(setting);
        SettingsService service = CreateService(repository, new SettingsProvider());

        await service.UpdateAsync(ValidUpdate(), CancellationToken.None);

        Assert.Equal("bb_session=old", setting.RutrackerCookie);
    }

    [Fact]
    public async Task InitializeAsync_WhenRowMissing_CreatesDefaultsAndAppliesSnapshot()
    {
        var repository = new InMemoryAppSettingsRepository(null);
        SettingsProvider provider = new();
        SettingsService service = CreateService(repository, provider);

        await service.InitializeAsync(CancellationToken.None);

        Assert.NotNull(repository.Setting);
        Assert.Equal(1, repository.SaveCount);
        Assert.Equal(AppSetting.DefaultScanIntervalMinutes, provider.Current.ScanIntervalMinutes);
        Assert.True(provider.Current.AutoScanEnabled);
        Assert.Equal(Now.UtcDateTime, provider.Current.UpdatedAtUtc);
    }

    [Fact]
    public async Task GetAsync_MasksCookieAndNeverExposesRawValue()
    {
        AppSetting setting = NewSetting();
        setting.RutrackerCookie = "bb_session=topsecret9876";
        var repository = new InMemoryAppSettingsRepository(setting);
        SettingsProvider provider = new();
        SettingsService service = CreateService(repository, provider);
        await service.InitializeAsync(CancellationToken.None);

        SettingsView view = service.Get();

        Assert.True(view.HasCookie);
        Assert.NotNull(view.MaskedCookie);
        Assert.DoesNotContain("topsecret", view.MaskedCookie);
        Assert.EndsWith("9876", view.MaskedCookie);
    }

    [Fact]
    public async Task TestCookieAsync_NoCookie_ReturnsFailureWithoutCallingRutracker()
    {
        var rutracker = new StubRutrackerClient(_ => throw new InvalidOperationException("не должен вызываться"));
        SettingsService service = CreateService(
            new InMemoryAppSettingsRepository(NewSetting()),
            new SettingsProvider(),
            rutracker);

        CookieTestResult result = await service.TestCookieAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Кука рутрекера не задана.", result.Message);
        Assert.Empty(rutracker.Requests);
    }

    [Fact]
    public async Task TestCookieAsync_NoTorrents_ReturnsFailureWithoutCallingRutracker()
    {
        AppSetting setting = NewSetting();
        setting.RutrackerCookie = "bb_session=secret";
        var rutracker = new StubRutrackerClient(_ => throw new InvalidOperationException("не должен вызываться"));
        SettingsService service = CreateService(
            new InMemoryAppSettingsRepository(setting),
            new SettingsProvider(),
            rutracker);

        CookieTestResult result = await service.TestCookieAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Нет отслеживаемых торрентов", result.Message);
        Assert.Empty(rutracker.Requests);
    }

    [Fact]
    public async Task TestCookieAsync_Success_UsesFirstTorrentAndMarksCookieVerified()
    {
        AppSetting setting = NewSetting();
        setting.RutrackerCookie = "bb_session=secret";
        setting.RequestTimeoutSeconds = 42;

        var repository = new InMemoryAppSettingsRepository(setting);
        SettingsProvider provider = new();
        var rutracker = new StubRutrackerClient(_ => Task.FromResult(new byte[] { 1, 2, 3 }));
        SettingsService service = CreateService(repository, provider, rutracker, Torrents(55, 99));

        CookieTestResult result = await service.TestCookieAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(Now.UtcDateTime, result.CheckedAtUtc);

        RutrackerDownloadRequest request = Assert.Single(rutracker.Requests);
        Assert.Equal(55, request.TopicId);
        Assert.Equal("bb_session=secret", request.Cookie);
        Assert.Equal(TimeSpan.FromSeconds(42), request.Timeout);

        Assert.Equal(Now.UtcDateTime, setting.CookieVerifiedAtUtc);
        Assert.Null(setting.CookieLastError);
        Assert.Equal(Now.UtcDateTime, provider.Current.CookieVerifiedAtUtc);
    }

    [Fact]
    public async Task TestCookieAsync_RutrackerRejectsCookie_RecordsLastError()
    {
        AppSetting setting = NewSetting();
        setting.RutrackerCookie = "bb_session=secret";

        var repository = new InMemoryAppSettingsRepository(setting);
        SettingsProvider provider = new();
        var rutracker = new StubRutrackerClient(_ => throw new RutrackerException(
            RutrackerFailureKind.CookieInvalid,
            "Рутрекер отклонил куку."));
        SettingsService service = CreateService(repository, provider, rutracker, Torrents(55));

        CookieTestResult result = await service.TestCookieAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Рутрекер отклонил куку.", result.Message);
        Assert.Null(setting.CookieVerifiedAtUtc);
        Assert.Equal("Рутрекер отклонил куку.", setting.CookieLastError);
        Assert.Equal("Рутрекер отклонил куку.", provider.Current.CookieLastError);
    }

    private static SettingsService CreateService(
        IAppSettingsRepository repository,
        ISettingsProvider provider,
        IRutrackerClient? rutracker = null,
        ITrackedTorrentRepository? torrents = null) =>
        new(
            repository,
            torrents ?? new InMemoryTrackedTorrentRepository(),
            rutracker ?? new StubRutrackerClient(_ => Task.FromResult(Array.Empty<byte>())),
            provider,
            new FixedTimeProvider(Now));

    private static SettingsUpdate ValidUpdate() => new(
        RutrackerCookie: null,
        ScanIntervalMinutes: 60,
        AutoScanEnabled: true,
        RequestDelayMs: 1500,
        RequestTimeoutSeconds: 30);

    private static AppSetting NewSetting() => new()
    {
        Id = AppSetting.DefaultId,
        ScanIntervalMinutes = AppSetting.DefaultScanIntervalMinutes,
        AutoScanEnabled = true,
        RequestDelayMs = AppSetting.DefaultRequestDelayMs,
        RequestTimeoutSeconds = AppSetting.DefaultRequestTimeoutSeconds,
        UpdatedAtUtc = Now.UtcDateTime,
    };

    private static InMemoryTrackedTorrentRepository Torrents(params int[] topicIds) =>
        new(topicIds.Select(topicId => new TrackedTorrent
        {
            Id = topicId,
            TopicId = topicId,
            TopicUrl = $"https://rutracker.org/forum/viewtopic.php?t={topicId}",
        }).ToArray());

    private static int AppSettingsSnapshotDefaultInterval() =>
        AppSettingsSnapshot.Default.ScanIntervalMinutes;
}
