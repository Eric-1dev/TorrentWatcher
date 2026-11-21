using TorrentWatcher.Application.Dtos;
using TorrentWatcher.Domain.Entities;

namespace TorrentWatcher.Application.Services;

/// <summary>
/// Собирает сводку для <c>GET /api/system/status</c>. Следующий запуск считается
/// от <see cref="AppSetting.LastScanAtUtc"/> и текущего интервала; на старте без
/// последнего скана он «уже наступил» — планировщик запустит скан сразу.
/// </summary>
public sealed class SystemStatusService
{
    private readonly TorrentService _torrentService;
    private readonly SettingsService _settingsService;
    private readonly ScanCoordinator _scanCoordinator;
    private readonly TimeProvider _timeProvider;

    public SystemStatusService(
        TorrentService torrentService,
        SettingsService settingsService,
        ScanCoordinator scanCoordinator,
        TimeProvider timeProvider)
    {
        _torrentService = torrentService;
        _settingsService = settingsService;
        _scanCoordinator = scanCoordinator;
        _timeProvider = timeProvider;
    }

    public async Task<SystemStatusView> GetAsync(CancellationToken cancellationToken)
    {
        TorrentListResult torrents = await _torrentService.GetAllAsync(cancellationToken);
        SettingsView settings = _settingsService.Get();
        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;

        DateTime? nextScanAt = null;
        if (settings.AutoScanEnabled)
        {
            DateTime dueAt = (settings.LastScanAtUtc ?? now).AddMinutes(settings.ScanIntervalMinutes);
            nextScanAt = dueAt < now ? now : dueAt;
        }

        return new SystemStatusView(
            Version: GetVersion(),
            CurrentRun: _scanCoordinator.Current,
            LastScanAtUtc: settings.LastScanAtUtc,
            NextScanAtUtc: nextScanAt,
            AutoScanEnabled: settings.AutoScanEnabled,
            ScanIntervalMinutes: settings.ScanIntervalMinutes,
            TorrentCount: torrents.TotalCount,
            UpdateAvailableCount: torrents.UpdateAvailableCount,
            CookieConfigured: settings.HasCredentials || settings.HasCookie,
            CookieVerifiedAtUtc: settings.CookieVerifiedAtUtc,
            CookieLastError: settings.CookieLastError);
    }

    private static string GetVersion() =>
        typeof(SystemStatusService).Assembly.GetName().Version?.ToString() ?? "unknown";
}
