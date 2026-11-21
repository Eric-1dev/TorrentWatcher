using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TorrentWatcher.Application.Abstractions;
using TorrentWatcher.Application.Dtos;
using TorrentWatcher.Application.Exceptions;
using TorrentWatcher.Application.Rutracker;
using TorrentWatcher.Application.Settings;
using TorrentWatcher.Domain.Entities;

namespace TorrentWatcher.Application.Services;

/// <summary>
/// Сеанс рутрекера: кэш куки в настройках и автоматический вход по логину и паролю,
/// когда кэша нет. Вход выполняет браузер (он уже нужен для Cloudflare), поэтому
/// пароль расшифровывается только на время вызова и никогда не логируется.
/// Singleton: сеанс и пауза после неудачи общие для API и планировщика.
/// </summary>
public sealed class RutrackerSessionService : IRutrackerSessionProvider
{
    private const int MaxErrorMessageLength = 4000;

    /// <summary>После неудачного входа не долбим рутрекер на каждом торренте скана.</summary>
    private static readonly TimeSpan FailureBackoff = TimeSpan.FromMinutes(10);

    /// <summary>Вход включает проверку Cloudflare, поэтому таймаута запроса может не хватить.</summary>
    private static readonly TimeSpan MinLoginTimeout = TimeSpan.FromSeconds(60);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRutrackerLogin _login;
    private readonly ISecretProtector _protector;
    private readonly ISettingsProvider _provider;
    private readonly SettingsWriteGate _writeGate;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RutrackerSessionService> _logger;
    private readonly SemaphoreSlim _gate = new(initialCount: 1, maxCount: 1);

    private DateTime? _lastFailureAtUtc;
    private RutrackerFailureKind? _lastFailureKind;
    private string? _lastFailureMessage;

    public RutrackerSessionService(
        IServiceScopeFactory scopeFactory,
        IRutrackerLogin login,
        ISecretProtector protector,
        ISettingsProvider provider,
        SettingsWriteGate writeGate,
        TimeProvider timeProvider,
        ILogger<RutrackerSessionService> logger)
    {
        _scopeFactory = scopeFactory;
        _login = login;
        _protector = protector;
        _provider = provider;
        _writeGate = writeGate;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<string> GetCookieAsync(CancellationToken cancellationToken)
    {
        AppSettingsSnapshot snapshot = _provider.Current;
        if (!string.IsNullOrWhiteSpace(snapshot.RutrackerCookie))
        {
            return snapshot.RutrackerCookie;
        }

        return await LoginCoreAsync(force: false, cancellationToken).ConfigureAwait(false);
    }

    public async Task<RutrackerLoginResult> LoginForUiAsync(CancellationToken cancellationToken)
    {
        try
        {
            string cookie = await LoginCoreAsync(force: true, cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(cookie))
            {
                return new RutrackerLoginResult(
                    Success: false,
                    Message: "Рутрекер не выдал куку сеанса после входа.",
                    LoggedInAtUtc: null);
            }

            return new RutrackerLoginResult(
                Success: true,
                Message: "Вход выполнен, сеанс сохранён.",
                LoggedInAtUtc: UtcNow());
        }
        catch (RutrackerException exception)
        {
            return new RutrackerLoginResult(
                Success: false,
                Message: exception.Message,
                LoggedInAtUtc: null);
        }
    }

    public void Reset()
    {
        _lastFailureAtUtc = null;
        _lastFailureKind = null;
        _lastFailureMessage = null;
    }

    private async Task<string> LoginCoreAsync(bool force, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            AppSettingsSnapshot snapshot = _provider.Current;

            // Пока ждали очередь, другой запрос мог уже выполнить вход.
            if (!force && !string.IsNullOrWhiteSpace(snapshot.RutrackerCookie))
            {
                return snapshot.RutrackerCookie;
            }

            ThrowIfBackoffActive(force);

            using IServiceScope scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IAppSettingsRepository>();

            AppSetting? setting = await repository
                .FindAsync(cancellationToken)
                .ConfigureAwait(false);

            if (setting is null)
            {
                throw new RutrackerException(
                    RutrackerFailureKind.CookieInvalid,
                    "Логин и пароль рутрекера не заданы. Укажите их в настройках.");
            }

            if (string.IsNullOrWhiteSpace(setting.RutrackerLogin)
                || string.IsNullOrWhiteSpace(setting.RutrackerPasswordProtected))
            {
                throw new RutrackerException(
                    RutrackerFailureKind.CookieInvalid,
                    "Логин и пароль рутрекера не заданы. Укажите их в настройках.");
            }

            string password = _protector.Unprotect(setting.RutrackerPasswordProtected)
                ?? throw new RutrackerException(
                    RutrackerFailureKind.CredentialsUnreadable,
                    "Сохранённый пароль рутрекера не читается: изменились или потерялись ключи "
                    + "шифрования. Введите пароль заново в настройках.");

            DateTime now = UtcNow();
            TimeSpan timeout = ComputeLoginTimeout(setting.RequestTimeoutSeconds);

            _logger.LogInformation(
                "Вхожу в рутрекер под пользователем {Login} через браузер.", setting.RutrackerLogin);

            RutrackerBrowserSession session;
            try
            {
                session = await _login
                    .LoginAsync(setting.RutrackerLogin, password, timeout, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (RutrackerException exception)
            {
                await RecordLoginFailureAsync(repository, setting, exception, now, cancellationToken)
                    .ConfigureAwait(false);

                throw;
            }

            if (string.IsNullOrWhiteSpace(session.Cookie))
            {
                throw new RutrackerException(
                    RutrackerFailureKind.LoginFailed,
                    "Рутрекер не выдал куку сеанса после входа.");
            }

            // Вход мог идти минуту: если за это время сменили логин или пароль, кука
            // относится к старым данным и сохранять её нельзя.
            using (await _writeGate.EnterAsync(cancellationToken).ConfigureAwait(false))
            {
                AppSetting? latest = await repository
                    .FindAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (latest is not null)
                {
                    if (!string.Equals(latest.RutrackerLogin, setting.RutrackerLogin, StringComparison.Ordinal)
                        || !string.Equals(
                            latest.RutrackerPasswordProtected,
                            setting.RutrackerPasswordProtected,
                            StringComparison.Ordinal))
                    {
                        throw new RutrackerException(
                            RutrackerFailureKind.LoginFailed,
                            "Логин или пароль изменились во время входа. Повторите проверку.");
                    }

                    setting = latest;
                }

                setting.RutrackerCookie = session.Cookie;
                setting.CookieVerifiedAtUtc = now;
                setting.CookieLastError = null;
                setting.UpdatedAtUtc = now;

                await repository.SaveAsync(setting, cancellationToken).ConfigureAwait(false);
                _provider.Apply(AppSettingsSnapshot.FromEntity(setting));
            }

            Reset();

            _logger.LogInformation(
                "Вход в рутрекер выполнен, сеанс сохранён. Пользователь: {Login}.",
                setting.RutrackerLogin);

            return setting.RutrackerCookie;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void ThrowIfBackoffActive(bool force)
    {
        if (force || _lastFailureAtUtc is not { } failedAt || _lastFailureKind is not { } kind)
        {
            return;
        }

        if (UtcNow() - failedAt < FailureBackoff)
        {
            throw new RutrackerException(
                kind,
                _lastFailureMessage ?? "Предыдущая попытка входа не удалась. Повторите позже.");
        }
    }

    private async Task RecordLoginFailureAsync(
        IAppSettingsRepository repository,
        AppSetting setting,
        RutrackerException exception,
        DateTime failedAtUtc,
        CancellationToken cancellationToken)
    {
        using (await _writeGate.EnterAsync(cancellationToken).ConfigureAwait(false))
        {
            AppSetting? latest = await repository
                .FindAsync(cancellationToken)
                .ConfigureAwait(false);

            // Пока шёл вход, пользователь мог сменить учётные данные. Тогда ошибка относится
            // к старым данным: не показываем её и не включаем паузу для новых.
            if (latest is not null
                && (!string.Equals(latest.RutrackerLogin, setting.RutrackerLogin, StringComparison.Ordinal)
                    || !string.Equals(
                        latest.RutrackerPasswordProtected,
                        setting.RutrackerPasswordProtected,
                        StringComparison.Ordinal)))
            {
                return;
            }

            _lastFailureAtUtc = failedAtUtc;
            _lastFailureKind = exception.Kind;
            _lastFailureMessage = exception.Message;

            setting = latest ?? setting;

            // Старую куку не стираем: принудительный вход мог не удаться, а прежний сеанс ещё жив.
            setting.CookieVerifiedAtUtc = null;
            setting.CookieLastError = Truncate(exception.Message);
            setting.UpdatedAtUtc = failedAtUtc;

            await repository.SaveAsync(setting, cancellationToken).ConfigureAwait(false);
            _provider.Apply(AppSettingsSnapshot.FromEntity(setting));
        }

        _logger.LogWarning(
            "Вход в рутрекер не выполнен ({Kind}): {Message}",
            exception.Kind,
            exception.Message);
    }

    private static TimeSpan ComputeLoginTimeout(int requestTimeoutSeconds)
    {
        TimeSpan requestTimeout = requestTimeoutSeconds > 0
            ? TimeSpan.FromSeconds(requestTimeoutSeconds)
            : TimeSpan.FromSeconds(AppSetting.DefaultRequestTimeoutSeconds);

        return requestTimeout > MinLoginTimeout ? requestTimeout : MinLoginTimeout;
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private static string Truncate(string value) =>
        value.Length <= MaxErrorMessageLength ? value : value[..MaxErrorMessageLength];
}
