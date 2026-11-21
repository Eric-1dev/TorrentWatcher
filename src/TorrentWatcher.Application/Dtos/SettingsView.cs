using TorrentWatcher.Application.Settings;

namespace TorrentWatcher.Application.Dtos;

/// <summary>
/// Настройки для UI. Пароли не отдаются никогда — только признак наличия. Сессионная кука
/// отдаётся маской; логин не секрет и показывается, чтобы было видно, под кем вход.
/// Для Cloudflare отдаётся статус пропуска, а не сама кука.
/// </summary>
public sealed record SettingsView(
    bool HasCookie,
    string? MaskedCookie,
    string? RutrackerLogin,
    bool HasPassword,
    bool HasCredentials,
    DateTime? CookieVerifiedAtUtc,
    string? CookieLastError,
    bool HasCloudflareClearance,
    string? CloudflareUserAgent,
    DateTime? CloudflareClearanceObtainedAtUtc,
    DateTime? CloudflareClearanceExpiresAtUtc,
    string? CloudflareLastError,
    int ScanIntervalMinutes,
    bool AutoScanEnabled,
    int RequestDelayMs,
    int RequestTimeoutSeconds,
    DateTime? LastScanAtUtc,
    DateTime UpdatedAtUtc,
    string? QbittorrentUrl,
    string? QbittorrentUsername,
    bool HasQbittorrentPassword,
    bool QbittorrentConfigured,
    bool QbittorrentAutoApply,
    DateTime? QbittorrentVerifiedAtUtc,
    string? QbittorrentLastError)
{
    public static SettingsView FromSnapshot(AppSettingsSnapshot settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        bool hasLogin = !string.IsNullOrWhiteSpace(settings.RutrackerLogin);
        bool hasPassword = !string.IsNullOrWhiteSpace(settings.RutrackerPasswordProtected);
        bool hasQbittorrentLogin = !string.IsNullOrWhiteSpace(settings.QbittorrentUsername);
        bool hasQbittorrentPassword = !string.IsNullOrWhiteSpace(settings.QbittorrentPasswordProtected);

        return new SettingsView(
            HasCookie: !string.IsNullOrWhiteSpace(settings.RutrackerCookie),
            MaskedCookie: MaskCookie(settings.RutrackerCookie),
            RutrackerLogin: hasLogin ? settings.RutrackerLogin : null,
            HasPassword: hasPassword,
            HasCredentials: hasLogin && hasPassword,
            settings.CookieVerifiedAtUtc,
            settings.CookieLastError,
            HasCloudflareClearance: !string.IsNullOrWhiteSpace(settings.CloudflareClearanceCookie),
            settings.CloudflareUserAgent,
            settings.CloudflareClearanceObtainedAtUtc,
            settings.CloudflareClearanceExpiresAtUtc,
            settings.CloudflareLastError,
            settings.ScanIntervalMinutes,
            settings.AutoScanEnabled,
            settings.RequestDelayMs,
            settings.RequestTimeoutSeconds,
            settings.LastScanAtUtc,
            settings.UpdatedAtUtc,
            QbittorrentUrl: NullIfEmpty(settings.QbittorrentUrl),
            QbittorrentUsername: hasQbittorrentLogin ? settings.QbittorrentUsername : null,
            HasQbittorrentPassword: hasQbittorrentPassword,
            QbittorrentConfigured: settings.QbittorrentConfigured,
            settings.QbittorrentAutoApply,
            settings.QbittorrentVerifiedAtUtc,
            settings.QbittorrentLastError);
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

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
