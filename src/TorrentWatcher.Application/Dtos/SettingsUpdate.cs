namespace TorrentWatcher.Application.Dtos;

/// <summary>
/// Данные формы настроек. <see cref="RutrackerCookie"/> = <c>null</c> означает
/// «не менять куку»: UI присылает её только когда пользователь ввёл новое значение.
/// </summary>
public sealed record SettingsUpdate(
    string? RutrackerCookie,
    int ScanIntervalMinutes,
    bool AutoScanEnabled,
    int RequestDelayMs,
    int RequestTimeoutSeconds);
