using TorrentWatcher.Application.Abstractions;
using TorrentWatcher.Application.Dtos;
using TorrentWatcher.Application.Exceptions;
using TorrentWatcher.Application.Rutracker;
using TorrentWatcher.Application.Settings;

namespace TorrentWatcher.UnitTests.Support;

/// <summary>
/// Заглушка сеанса рутрекера. По умолчанию отдаёт куку из настроек, а при пустой куке
/// ведёт себя как настоящий сервис: без логина и пароля бросает <c>RUTRACKER_COOKIE_INVALID</c>.
/// </summary>
public sealed class StubRutrackerSessionProvider : IRutrackerSessionProvider
{
    private readonly Func<Task<string>> _handler;

    public StubRutrackerSessionProvider(Func<Task<string>> handler)
    {
        _handler = handler;
    }

    public int GetCookieCalls { get; private set; }

    public static StubRutrackerSessionProvider FromSettings(ISettingsProvider provider) =>
        FromCookieFactory(() => provider.Current.RutrackerCookie);

    /// <summary>Для тестов, где снапшот ещё не инициализирован, а кука лежит в репозитории.</summary>
    public static StubRutrackerSessionProvider FromCookieFactory(Func<string?> cookieFactory) =>
        new(() =>
        {
            string? cookie = cookieFactory();

            if (string.IsNullOrWhiteSpace(cookie))
            {
                return Task.FromException<string>(new RutrackerException(
                    RutrackerFailureKind.CookieInvalid,
                    "Логин и пароль рутрекера не заданы. Укажите их в настройках."));
            }

            return Task.FromResult(cookie);
        });

    public Task<string> GetCookieAsync(CancellationToken cancellationToken)
    {
        GetCookieCalls++;
        return _handler();
    }

    public Task<RutrackerLoginResult> LoginForUiAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException("В unit-тестах вход через UI не используется.");

    public void Reset()
    {
    }
}
