using TorrentWatcher.Domain.Entities;

namespace TorrentWatcher.Application.Settings;

/// <summary>
/// Неизменяемый снапшот настроек для чтения «на лету»: планировщик и HTTP-запросы
/// берут его без обращения к БД. Источник истины — строка <see cref="AppSetting"/>.
/// </summary>
public sealed record AppSettingsSnapshot(
    string RutrackerCookie,
    DateTime? CookieVerifiedAtUtc,
    string? CookieLastError,
    int ScanIntervalMinutes,
    bool AutoScanEnabled,
    int RequestDelayMs,
    int RequestTimeoutSeconds,
    DateTime? LastScanAtUtc,
    DateTime UpdatedAtUtc)
{
    /// <summary>Значения по умолчанию до первой загрузки из БД.</summary>
    public static AppSettingsSnapshot Default { get; } = new(
        RutrackerCookie: string.Empty,
        CookieVerifiedAtUtc: null,
        CookieLastError: null,
        ScanIntervalMinutes: AppSetting.DefaultScanIntervalMinutes,
        AutoScanEnabled: true,
        RequestDelayMs: AppSetting.DefaultRequestDelayMs,
        RequestTimeoutSeconds: AppSetting.DefaultRequestTimeoutSeconds,
        LastScanAtUtc: null,
        UpdatedAtUtc: DateTime.MinValue);

    public TimeSpan RequestTimeout => TimeSpan.FromSeconds(RequestTimeoutSeconds);

    public static AppSettingsSnapshot FromEntity(AppSetting setting)
    {
        ArgumentNullException.ThrowIfNull(setting);

        return new AppSettingsSnapshot(
            setting.RutrackerCookie,
            setting.CookieVerifiedAtUtc,
            setting.CookieLastError,
            setting.ScanIntervalMinutes,
            setting.AutoScanEnabled,
            setting.RequestDelayMs,
            setting.RequestTimeoutSeconds,
            setting.LastScanAtUtc,
            setting.UpdatedAtUtc);
    }
}
