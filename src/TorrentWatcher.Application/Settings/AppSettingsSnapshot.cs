using TorrentWatcher.Domain.Entities;

namespace TorrentWatcher.Application.Settings;

/// <summary>
/// Неизменяемый снапшот настроек для чтения «на лету»: планировщик и HTTP-запросы
/// берут его без обращения к БД. Источник истины — строка <see cref="AppSetting"/>.
/// </summary>
public sealed record AppSettingsSnapshot(
    string RutrackerCookie,
    string RutrackerLogin,
    string RutrackerPasswordProtected,
    DateTime? CookieVerifiedAtUtc,
    string? CookieLastError,
    string? CloudflareClearanceCookie,
    string? CloudflareUserAgent,
    DateTime? CloudflareClearanceObtainedAtUtc,
    DateTime? CloudflareClearanceExpiresAtUtc,
    string? CloudflareLastError,
    int ScanIntervalMinutes,
    bool AutoScanEnabled,
    int RequestDelayMs,
    int RequestTimeoutSeconds,
    DateTime? LastScanAtUtc,
    bool QbittorrentAutoApply,
    string QbittorrentUrl,
    string QbittorrentUsername,
    string QbittorrentPasswordProtected,
    DateTime? QbittorrentVerifiedAtUtc,
    string? QbittorrentLastError,
    DateTime UpdatedAtUtc)
{
    /// <summary>Значения по умолчанию до первой загрузки из БД.</summary>
    public static AppSettingsSnapshot Default { get; } = new(
        RutrackerCookie: string.Empty,
        RutrackerLogin: string.Empty,
        RutrackerPasswordProtected: string.Empty,
        CookieVerifiedAtUtc: null,
        CookieLastError: null,
        CloudflareClearanceCookie: null,
        CloudflareUserAgent: null,
        CloudflareClearanceObtainedAtUtc: null,
        CloudflareClearanceExpiresAtUtc: null,
        CloudflareLastError: null,
        ScanIntervalMinutes: AppSetting.DefaultScanIntervalMinutes,
        AutoScanEnabled: true,
        RequestDelayMs: AppSetting.DefaultRequestDelayMs,
        RequestTimeoutSeconds: AppSetting.DefaultRequestTimeoutSeconds,
        LastScanAtUtc: null,
        QbittorrentAutoApply: false,
        QbittorrentUrl: string.Empty,
        QbittorrentUsername: string.Empty,
        QbittorrentPasswordProtected: string.Empty,
        QbittorrentVerifiedAtUtc: null,
        QbittorrentLastError: null,
        UpdatedAtUtc: DateTime.MinValue);

    public TimeSpan RequestTimeout => TimeSpan.FromSeconds(RequestTimeoutSeconds);

    /// <summary>Заданы ли адрес, логин и пароль qBittorrent.</summary>
    public bool QbittorrentConfigured =>
        !string.IsNullOrWhiteSpace(QbittorrentUrl)
        && !string.IsNullOrWhiteSpace(QbittorrentUsername)
        && !string.IsNullOrWhiteSpace(QbittorrentPasswordProtected);

    public static AppSettingsSnapshot FromEntity(AppSetting setting)
    {
        ArgumentNullException.ThrowIfNull(setting);

        return new AppSettingsSnapshot(
            setting.RutrackerCookie,
            setting.RutrackerLogin,
            setting.RutrackerPasswordProtected,
            setting.CookieVerifiedAtUtc,
            setting.CookieLastError,
            setting.CloudflareClearanceCookie,
            setting.CloudflareUserAgent,
            setting.CloudflareClearanceObtainedAtUtc,
            setting.CloudflareClearanceExpiresAtUtc,
            setting.CloudflareLastError,
            setting.ScanIntervalMinutes,
            setting.AutoScanEnabled,
            setting.RequestDelayMs,
            setting.RequestTimeoutSeconds,
            setting.LastScanAtUtc,
            setting.QbittorrentAutoApply,
            setting.QbittorrentUrl,
            setting.QbittorrentUsername,
            setting.QbittorrentPasswordProtected,
            setting.QbittorrentVerifiedAtUtc,
            setting.QbittorrentLastError,
            setting.UpdatedAtUtc);
    }
}
