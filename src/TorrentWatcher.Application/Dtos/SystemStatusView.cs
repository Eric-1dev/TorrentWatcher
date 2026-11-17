using TorrentWatcher.Application.State;

namespace TorrentWatcher.Application.Dtos;

/// <summary>
/// Сводка для шапки UI: версия, состояние скана, расписание, счётчики и кука.
/// Следующий запуск рассчитывается по последнему скану и текущему интервалу.
/// </summary>
public sealed record SystemStatusView(
    string Version,
    ScanStatus? CurrentRun,
    DateTime? LastScanAtUtc,
    DateTime? NextScanAtUtc,
    bool AutoScanEnabled,
    int ScanIntervalMinutes,
    int TorrentCount,
    int UpdateAvailableCount,
    bool CookieConfigured,
    DateTime? CookieVerifiedAtUtc,
    string? CookieLastError);
