using TorrentWatcher.Application.Abstractions;
using TorrentWatcher.Application.Rutracker;

namespace TorrentWatcher.UnitTests.Support;

/// <summary>Заглушка браузерного входа: запоминает логин/пароль и отдаёт заданную куку.</summary>
public sealed class StubRutrackerLogin(
    Func<string, string, Task<RutrackerBrowserSession>> handler) : IRutrackerLogin
{
    public int CallCount { get; private set; }

    public string? LastLogin { get; private set; }

    public string? LastPassword { get; private set; }

    public Task<RutrackerBrowserSession> LoginAsync(
        string login,
        string password,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        CallCount++;
        LastLogin = login;
        LastPassword = password;
        return handler(login, password);
    }
}
