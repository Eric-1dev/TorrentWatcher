using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TorrentWatcher.Application.Abstractions;
using TorrentWatcher.Application.Exceptions;
using TorrentWatcher.Application.Rutracker;
using TorrentWatcher.Application.Services;
using TorrentWatcher.Application.Settings;
using TorrentWatcher.Domain.Entities;
using TorrentWatcher.UnitTests.Support;

namespace TorrentWatcher.UnitTests.Application.Settings;

public sealed class RutrackerSessionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetCookieAsync_CachedCookie_DoesNotCallLogin()
    {
        AppSetting setting = Setting(cookie: "bb_session=cached", login: "user", password: "enc:secret");
        var repository = new InMemoryAppSettingsRepository(setting);
        SettingsProvider provider = ProviderWith(setting);
        var login = new StubRutrackerLogin((_, _) => throw new InvalidOperationException("вход не нужен"));
        RutrackerSessionService service = CreateService(repository, login, provider);

        string cookie = await service.GetCookieAsync(CancellationToken.None);

        Assert.Equal("bb_session=cached", cookie);
        Assert.Equal(0, login.CallCount);
    }

    [Fact]
    public async Task GetCookieAsync_NoCookie_LogsInAndSavesCookie()
    {
        AppSetting setting = Setting(cookie: string.Empty, login: "user", password: "enc:secret");
        var repository = new InMemoryAppSettingsRepository(setting);
        SettingsProvider provider = ProviderWith(setting);
        var login = new StubRutrackerLogin(
            (_, _) => Task.FromResult(new RutrackerBrowserSession("bb_session=new")));
        var time = new FakeTimeProvider(Now);
        RutrackerSessionService service = CreateService(repository, login, provider, time);

        string cookie = await service.GetCookieAsync(CancellationToken.None);

        Assert.Equal("bb_session=new", cookie);
        Assert.Equal(1, login.CallCount);
        Assert.Equal("user", login.LastLogin);
        // Пароль расшифрован перед передачей в браузер.
        Assert.Equal("secret", login.LastPassword);

        Assert.Equal("bb_session=new", setting.RutrackerCookie);
        Assert.Equal("bb_session=new", provider.Current.RutrackerCookie);
        Assert.Equal(Now.UtcDateTime, setting.CookieVerifiedAtUtc);
        Assert.Null(setting.CookieLastError);
    }

    [Fact]
    public async Task GetCookieAsync_NoCredentials_ThrowsCookieInvalidWithoutLogin()
    {
        AppSetting setting = Setting(cookie: string.Empty);
        var repository = new InMemoryAppSettingsRepository(setting);
        SettingsProvider provider = ProviderWith(setting);
        var login = new StubRutrackerLogin((_, _) => throw new InvalidOperationException("вход не нужен"));
        RutrackerSessionService service = CreateService(repository, login, provider);

        RutrackerException exception = await Assert.ThrowsAsync<RutrackerException>(
            () => service.GetCookieAsync(CancellationToken.None));

        Assert.Equal(ErrorCodes.RutrackerCookieInvalid, exception.Code);
        Assert.Equal(0, login.CallCount);
    }

    [Fact]
    public async Task GetCookieAsync_UnreadablePassword_ThrowsCredentialsUnreadable()
    {
        AppSetting setting = Setting(cookie: string.Empty, login: "user", password: "broken");
        var repository = new InMemoryAppSettingsRepository(setting);
        SettingsProvider provider = ProviderWith(setting);
        var login = new StubRutrackerLogin((_, _) => throw new InvalidOperationException("вход не нужен"));
        RutrackerSessionService service = CreateService(repository, login, provider);

        RutrackerException exception = await Assert.ThrowsAsync<RutrackerException>(
            () => service.GetCookieAsync(CancellationToken.None));

        Assert.Equal(ErrorCodes.RutrackerCredentialsUnreadable, exception.Code);
        Assert.Equal(0, login.CallCount);
    }

    [Fact]
    public async Task GetCookieAsync_LoginFailure_RetriesOnlyAfterBackoff()
    {
        AppSetting setting = Setting(cookie: string.Empty, login: "user", password: "enc:secret");
        var repository = new InMemoryAppSettingsRepository(setting);
        SettingsProvider provider = ProviderWith(setting);
        var login = new StubRutrackerLogin(
            (_, _) => throw new RutrackerException(RutrackerFailureKind.LoginFailed, "Пароль неверный."));
        var time = new FakeTimeProvider(Now);
        RutrackerSessionService service = CreateService(repository, login, provider, time);

        await Assert.ThrowsAsync<RutrackerException>(() => service.GetCookieAsync(CancellationToken.None));
        await Assert.ThrowsAsync<RutrackerException>(() => service.GetCookieAsync(CancellationToken.None));

        Assert.Equal(1, login.CallCount);
        Assert.Equal("Пароль неверный.", setting.CookieLastError);

        // Через окно паузы попытка повторяется.
        time.Advance(TimeSpan.FromMinutes(11));
        await Assert.ThrowsAsync<RutrackerException>(() => service.GetCookieAsync(CancellationToken.None));

        Assert.Equal(2, login.CallCount);
    }

    [Fact]
    public async Task LoginForUiAsync_Captcha_ReturnsFailureAndBypassesBackoff()
    {
        AppSetting setting = Setting(cookie: string.Empty, login: "user", password: "enc:secret");
        var repository = new InMemoryAppSettingsRepository(setting);
        SettingsProvider provider = ProviderWith(setting);
        var login = new StubRutrackerLogin(
            (_, _) => throw new RutrackerException(
                RutrackerFailureKind.LoginCaptcha,
                "Рутрекер требует капчу."));
        RutrackerSessionService service = CreateService(repository, login, provider);

        var first = await service.LoginForUiAsync(CancellationToken.None);
        var second = await service.LoginForUiAsync(CancellationToken.None);

        Assert.False(first.Success);
        Assert.Contains("капчу", first.Message);
        Assert.False(second.Success);
        // Кнопка в UI всегда пробует заново, пауза её не блокирует.
        Assert.Equal(2, login.CallCount);
        Assert.Equal("Рутрекер требует капчу.", setting.CookieLastError);
    }

    [Fact]
    public async Task LoginForUiAsync_Success_ReturnsTimestampAndSession()
    {
        AppSetting setting = Setting(cookie: string.Empty, login: "user", password: "enc:secret");
        var repository = new InMemoryAppSettingsRepository(setting);
        SettingsProvider provider = ProviderWith(setting);
        var login = new StubRutrackerLogin(
            (_, _) => Task.FromResult(new RutrackerBrowserSession("bb_session=ui")));
        var time = new FakeTimeProvider(Now);
        RutrackerSessionService service = CreateService(repository, login, provider, time);

        var result = await service.LoginForUiAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(Now.UtcDateTime, result.LoggedInAtUtc);
        Assert.Equal("bb_session=ui", provider.Current.RutrackerCookie);
    }

    private static AppSetting Setting(
        string cookie = "bb_session=secret",
        string login = "",
        string password = "") => new()
        {
            Id = AppSetting.DefaultId,
            RutrackerCookie = cookie,
            RutrackerLogin = login,
            RutrackerPasswordProtected = password,
            RequestTimeoutSeconds = 30,
            UpdatedAtUtc = Now.UtcDateTime,
        };

    private static SettingsProvider ProviderWith(AppSetting setting)
    {
        var provider = new SettingsProvider();
        provider.Apply(AppSettingsSnapshot.FromEntity(setting));
        return provider;
    }

    private static RutrackerSessionService CreateService(
        IAppSettingsRepository repository,
        IRutrackerLogin login,
        SettingsProvider provider,
        TimeProvider? timeProvider = null) =>
        new(
            new SingleServiceScopeFactory<IAppSettingsRepository>(repository),
            login,
            new StubSecretProtector(),
            provider,
            new SettingsWriteGate(),
            timeProvider ?? new FixedTimeProvider(Now),
            NullLogger<RutrackerSessionService>.Instance);

    /// <summary>Минимальный scope factory: в тесте нужен только репозиторий настроек.</summary>
    private sealed class SingleServiceScopeFactory<T>(T instance) : IServiceScopeFactory, IServiceScope
    {
        public IServiceProvider ServiceProvider { get; } = new SingleServiceProvider<T>(instance);

        public IServiceScope CreateScope() => this;

        public void Dispose()
        {
        }
    }

    private sealed class SingleServiceProvider<T>(T instance) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(T) ? instance : null;
    }
}
