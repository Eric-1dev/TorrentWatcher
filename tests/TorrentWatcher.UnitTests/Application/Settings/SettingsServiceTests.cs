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
    public async Task TestCookieAsync_NoCredentials_ReturnsFailureWithoutCallingRutracker()
    {
        var rutracker = new StubRutrackerClient(_ => throw new InvalidOperationException("не должен вызываться"));
        SettingsService service = CreateService(
            new InMemoryAppSettingsRepository(NewSetting()),
            new SettingsProvider(),
            rutracker,
            Torrents(55));

        CookieTestResult result = await service.TestCookieAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Логин и пароль", result.Message);
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

    [Fact]
    public async Task UpdateAsync_ManualClearance_SavesCookieAndUserAgent()
    {
        AppSetting setting = NewSetting();
        var repository = new InMemoryAppSettingsRepository(setting);
        SettingsProvider provider = new();
        SettingsService service = CreateService(repository, provider);

        SettingsView view = await service.UpdateAsync(
            ValidUpdate() with
            {
                CloudflareClearanceCookie = "cf_clearance=manual",
                CloudflareUserAgent = "UA-manual",
            },
            CancellationToken.None);

        Assert.Equal("cf_clearance=manual", setting.CloudflareClearanceCookie);
        Assert.Equal("UA-manual", setting.CloudflareUserAgent);
        Assert.Equal(Now.UtcDateTime, setting.CloudflareClearanceObtainedAtUtc);
        Assert.Null(setting.CloudflareClearanceExpiresAtUtc);
        Assert.Null(setting.CloudflareLastError);
        Assert.True(view.HasCloudflareClearance);
        Assert.Equal("UA-manual", provider.Current.CloudflareUserAgent);
    }

    [Fact]
    public async Task UpdateAsync_EmptyClearance_RemovesStoredClearance()
    {
        AppSetting setting = NewSetting();
        setting.CloudflareClearanceCookie = "cf_clearance=old";
        setting.CloudflareClearanceObtainedAtUtc = Now.UtcDateTime.AddDays(-1);
        setting.CloudflareLastError = "старая ошибка";

        var repository = new InMemoryAppSettingsRepository(setting);
        SettingsService service = CreateService(repository, new SettingsProvider());

        SettingsView view = await service.UpdateAsync(
            ValidUpdate() with { CloudflareClearanceCookie = string.Empty },
            CancellationToken.None);

        Assert.Null(setting.CloudflareClearanceCookie);
        Assert.Null(setting.CloudflareClearanceObtainedAtUtc);
        Assert.Null(setting.CloudflareLastError);
        Assert.False(view.HasCloudflareClearance);
    }

    [Fact]
    public async Task UpdateAsync_UserAgentChangeWithoutNewClearance_InvalidatesClearance()
    {
        AppSetting setting = NewSetting();
        setting.CloudflareClearanceCookie = "cf_clearance=old";
        setting.CloudflareUserAgent = "UA-old";
        setting.CloudflareClearanceExpiresAtUtc = Now.UtcDateTime.AddDays(1);

        var repository = new InMemoryAppSettingsRepository(setting);
        SettingsService service = CreateService(repository, new SettingsProvider());

        await service.UpdateAsync(
            ValidUpdate() with { CloudflareUserAgent = "UA-new" },
            CancellationToken.None);

        Assert.Equal("UA-new", setting.CloudflareUserAgent);
        Assert.Null(setting.CloudflareClearanceCookie);
        Assert.Null(setting.CloudflareClearanceExpiresAtUtc);
    }

    [Fact]
    public async Task UpdateAsync_SameUserAgent_KeepsClearance()
    {
        AppSetting setting = NewSetting();
        setting.CloudflareClearanceCookie = "cf_clearance=old";
        setting.CloudflareUserAgent = "UA-old";

        var repository = new InMemoryAppSettingsRepository(setting);
        SettingsService service = CreateService(repository, new SettingsProvider());

        await service.UpdateAsync(
            ValidUpdate() with { CloudflareUserAgent = "UA-old" },
            CancellationToken.None);

        Assert.Equal("cf_clearance=old", setting.CloudflareClearanceCookie);
    }

    [Fact]
    public async Task UpdateAsync_Credentials_ProtectPasswordAndExposeOnlyFlags()
    {
        AppSetting setting = NewSetting();
        var repository = new InMemoryAppSettingsRepository(setting);
        SettingsProvider provider = new();
        SettingsService service = CreateService(repository, provider);

        SettingsView view = await service.UpdateAsync(
            ValidUpdate() with { RutrackerLogin = " user ", RutrackerPassword = "secret" },
            CancellationToken.None);

        Assert.Equal("user", setting.RutrackerLogin);
        Assert.Equal("enc:secret", setting.RutrackerPasswordProtected);
        Assert.Equal("user", view.RutrackerLogin);
        Assert.True(view.HasPassword);
        Assert.True(view.HasCredentials);
        Assert.Equal("user", provider.Current.RutrackerLogin);
    }

    [Fact]
    public async Task UpdateAsync_LoginChange_DropsCachedSessionButKeepsPassword()
    {
        AppSetting setting = NewSetting();
        setting.RutrackerCookie = "bb_session=old";
        setting.RutrackerLogin = "old";
        setting.RutrackerPasswordProtected = "enc:secret";
        setting.CookieVerifiedAtUtc = Now.UtcDateTime.AddDays(-1);
        setting.CookieLastError = "старая ошибка";

        var repository = new InMemoryAppSettingsRepository(setting);
        SettingsService service = CreateService(repository, new SettingsProvider());

        SettingsView view = await service.UpdateAsync(
            ValidUpdate() with { RutrackerLogin = "new" },
            CancellationToken.None);

        Assert.Equal("new", setting.RutrackerLogin);
        Assert.Equal("enc:secret", setting.RutrackerPasswordProtected);
        Assert.Equal(string.Empty, setting.RutrackerCookie);
        Assert.Null(setting.CookieVerifiedAtUtc);
        Assert.Null(setting.CookieLastError);
        Assert.False(view.HasCookie);
    }

    [Fact]
    public async Task UpdateAsync_EmptyPassword_ClearsCredentials()
    {
        AppSetting setting = NewSetting();
        setting.RutrackerLogin = "user";
        setting.RutrackerPasswordProtected = "enc:secret";
        var repository = new InMemoryAppSettingsRepository(setting);
        SettingsService service = CreateService(repository, new SettingsProvider());

        SettingsView view = await service.UpdateAsync(
            ValidUpdate() with { RutrackerPassword = string.Empty },
            CancellationToken.None);

        Assert.Equal(string.Empty, setting.RutrackerPasswordProtected);
        Assert.False(view.HasPassword);
        Assert.False(view.HasCredentials);
    }

    [Fact]
    public async Task UpdateAsync_EmptyLogin_ClearsPasswordToo()
    {
        AppSetting setting = NewSetting();
        setting.RutrackerLogin = "user";
        setting.RutrackerPasswordProtected = "enc:secret";
        var repository = new InMemoryAppSettingsRepository(setting);
        SettingsService service = CreateService(repository, new SettingsProvider());

        SettingsView view = await service.UpdateAsync(
            ValidUpdate() with { RutrackerLogin = string.Empty },
            CancellationToken.None);

        Assert.Equal(string.Empty, setting.RutrackerLogin);
        Assert.Equal(string.Empty, setting.RutrackerPasswordProtected);
        Assert.False(view.HasCredentials);
    }

    [Fact]
    public async Task UpdateAsync_CredentialsTooLong_ThrowsAndDoesNotSave()
    {
        var repository = new InMemoryAppSettingsRepository(NewSetting());
        SettingsService service = CreateService(repository, new SettingsProvider());

        AppException exception = await Assert.ThrowsAsync<AppException>(
            () => service.UpdateAsync(
                ValidUpdate() with { RutrackerPassword = new string('x', AppSetting.MaxPasswordLength + 1) },
                CancellationToken.None));

        Assert.Equal(ErrorCodes.SettingsInvalid, exception.Code);
        Assert.Equal(0, repository.SaveCount);
    }

    [Fact]
    public async Task UpdateAsync_Qbittorrent_ProtectsPasswordAndResetsVerification()
    {
        AppSetting setting = NewSetting();
        setting.QbittorrentVerifiedAtUtc = Now.UtcDateTime.AddHours(-1);
        setting.QbittorrentLastError = "старая ошибка";

        var repository = new InMemoryAppSettingsRepository(setting);
        SettingsService service = CreateService(repository, new SettingsProvider());

        SettingsView view = await service.UpdateAsync(
            ValidUpdate() with
            {
                QbittorrentUrl = "http://plex.local:8080/",
                QbittorrentUsername = " admin ",
                QbittorrentPassword = "secret",
                QbittorrentAutoApply = true,
            },
            CancellationToken.None);

        Assert.Equal("http://plex.local:8080", setting.QbittorrentUrl);
        Assert.Equal("admin", setting.QbittorrentUsername);
        Assert.Equal("enc:secret", setting.QbittorrentPasswordProtected);
        Assert.True(setting.QbittorrentAutoApply);
        Assert.Null(setting.QbittorrentVerifiedAtUtc);
        Assert.Null(setting.QbittorrentLastError);
        Assert.True(view.QbittorrentConfigured);
        Assert.True(view.HasQbittorrentPassword);
    }

    [Fact]
    public async Task UpdateAsync_QbittorrentInvalidUrl_ThrowsAndDoesNotSave()
    {
        var repository = new InMemoryAppSettingsRepository(NewSetting());
        SettingsService service = CreateService(repository, new SettingsProvider());

        AppException exception = await Assert.ThrowsAsync<AppException>(
            () => service.UpdateAsync(
                ValidUpdate() with { QbittorrentUrl = "plex.local:8080" },
                CancellationToken.None));

        Assert.Equal(ErrorCodes.SettingsInvalid, exception.Code);
        Assert.Equal(0, repository.SaveCount);
    }

    [Fact]
    public async Task TestQbittorrentAsync_Success_SavesVerification()
    {
        AppSetting setting = NewSetting();
        setting.QbittorrentUrl = "http://plex.local:8080";
        setting.QbittorrentUsername = "admin";
        setting.QbittorrentPasswordProtected = "enc:secret";

        var repository = new InMemoryAppSettingsRepository(setting);
        SettingsProvider provider = ProviderFrom(setting);
        var client = new RecordingTorrentClient { Version = "v5.0.3" };
        SettingsService service = CreateService(repository, provider, torrentClient: client);

        TorrentClientTestResult result = await service.TestQbittorrentAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("v5.0.3", result.Message);
        Assert.Equal(Now.UtcDateTime, setting.QbittorrentVerifiedAtUtc);
        Assert.Null(setting.QbittorrentLastError);
        Assert.Equal(Now.UtcDateTime, provider.Current.QbittorrentVerifiedAtUtc);
    }

    [Fact]
    public async Task TestQbittorrentAsync_Failure_SavesError()
    {
        AppSetting setting = NewSetting();
        setting.QbittorrentUrl = "http://plex.local:8080";
        setting.QbittorrentUsername = "admin";
        setting.QbittorrentPasswordProtected = "enc:secret";

        var repository = new InMemoryAppSettingsRepository(setting);
        var client = new RecordingTorrentClient
        {
            VersionException = new AppException(
                ErrorCodes.QbittorrentAuthFailed,
                "qBittorrent отклонил логин или пароль."),
        };

        SettingsService service = CreateService(
            repository,
            ProviderFrom(setting),
            torrentClient: client);

        TorrentClientTestResult result = await service.TestQbittorrentAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(setting.QbittorrentVerifiedAtUtc);
        Assert.Equal("qBittorrent отклонил логин или пароль.", setting.QbittorrentLastError);
    }

    [Fact]
    public async Task TestQbittorrentAsync_NotConfigured_DoesNotCallClient()
    {
        var repository = new InMemoryAppSettingsRepository(NewSetting());
        var client = new RecordingTorrentClient();
        SettingsService service = CreateService(
            repository,
            new SettingsProvider(),
            torrentClient: client);

        TorrentClientTestResult result = await service.TestQbittorrentAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Укажите адрес", result.Message);
        Assert.Empty(client.Calls);
    }

    private static SettingsProvider ProviderFrom(AppSetting setting)
    {
        var provider = new SettingsProvider();
        provider.Apply(AppSettingsSnapshot.FromEntity(setting));
        return provider;
    }

    private static SettingsService CreateService(
        InMemoryAppSettingsRepository repository,
        ISettingsProvider provider,
        IRutrackerClient? rutracker = null,
        ITrackedTorrentRepository? torrents = null,
        ITorrentClient? torrentClient = null) =>
        new(
            repository,
            torrents ?? new InMemoryTrackedTorrentRepository(),
            rutracker ?? new StubRutrackerClient(_ => Task.FromResult(Array.Empty<byte>())),
            torrentClient ?? new RecordingTorrentClient(),
            StubRutrackerSessionProvider.FromCookieFactory(() => repository.Setting?.RutrackerCookie),
            new StubSecretProtector(),
            provider,
            new SettingsWriteGate(),
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
