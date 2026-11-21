using TorrentWatcher.Application.Rutracker;

namespace TorrentWatcher.Application.Abstractions;

/// <summary>
/// Вход в рутрекер по логину и паролю в браузере с persistent-профилем. Браузер уже
/// используется для Cloudflare, поэтому именно он выполняет вход и хранит сеанс.
/// </summary>
public interface IRutrackerLogin
{
    /// <summary>
    /// Открывает <c>login.php</c> и входит в аккаунт. Если в профиле уже действует сеанс,
    /// возвращает его куку без повторной отправки формы.
    /// </summary>
    /// <exception cref="Exceptions.RutrackerException">
    /// <c>RUTRACKER_LOGIN_FAILED</c> — неверные данные, <c>RUTRACKER_LOGIN_CAPTCHA_REQUIRED</c> —
    /// рутрекер требует капчу, <c>RUTRACKER_CHALLENGE_REQUIRED</c> — не пройден Cloudflare.
    /// </exception>
    Task<RutrackerBrowserSession> LoginAsync(
        string login,
        string password,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}
