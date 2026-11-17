using TorrentWatcher.Application.Settings;

namespace TorrentWatcher.Application.Dtos;

/// <summary>
/// Настройки для UI. Сама кука не отдаётся никогда: только факт наличия и маска
/// из последних четырёх символов.
/// </summary>
public sealed record SettingsView(
    bool HasCookie,
    string? MaskedCookie,
    DateTime? CookieVerifiedAtUtc,
    string? CookieLastError,
    int ScanIntervalMinutes,
    bool AutoScanEnabled,
    int RequestDelayMs,
    int RequestTimeoutSeconds,
    DateTime? LastScanAtUtc,
    DateTime UpdatedAtUtc)
{
    public static SettingsView FromSnapshot(AppSettingsSnapshot settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new SettingsView(
            HasCookie: !string.IsNullOrWhiteSpace(settings.RutrackerCookie),
            MaskedCookie: MaskCookie(settings.RutrackerCookie),
            settings.CookieVerifiedAtUtc,
            settings.CookieLastError,
            settings.ScanIntervalMinutes,
            settings.AutoScanEnabled,
            settings.RequestDelayMs,
            settings.RequestTimeoutSeconds,
            settings.LastScanAtUtc,
            settings.UpdatedAtUtc);
    }

    private static string? MaskCookie(string cookie)
    {
        string value = cookie.Trim();
        if (value.Length == 0)
        {
            return null;
        }

        const int visibleChars = 4;
        return value.Length <= visibleChars ? "••••" : "••••" + value[^visibleChars..];
    }
}
