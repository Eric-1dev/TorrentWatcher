using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TorrentWatcher.Application.Abstractions;
using TorrentWatcher.Application.Settings;
using TorrentWatcher.Application.State;

namespace TorrentWatcher.Application.Services;

/// <summary>
/// Фоновый автоскан. Ждёт до «последний скан + интервал», пересчитывает ожидание
/// при каждом изменении настроек и завершается по stopping token. Если в этот момент
/// идёт ручной скан — автоскан пропускается, а следующий расчёт идёт от его финиша.
/// </summary>
public sealed class ScanScheduler : BackgroundService
{
    private readonly ScanCoordinator _coordinator;
    private readonly ISettingsProvider _settingsProvider;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ScanScheduler> _logger;

    public ScanScheduler(
        ScanCoordinator coordinator,
        ISettingsProvider settingsProvider,
        TimeProvider timeProvider,
        ILogger<ScanScheduler> logger)
    {
        _coordinator = coordinator;
        _settingsProvider = settingsProvider;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Планировщик автоскана запущен.");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                AppSettingsSnapshot settings = _settingsProvider.Current;

                if (!settings.AutoScanEnabled)
                {
                    await WaitWhileAutoScanDisabledAsync(stoppingToken).ConfigureAwait(false);
                    continue;
                }

                TimeSpan delay = ComputeDelay(settings, _timeProvider.GetUtcNow().UtcDateTime);
                _logger.LogDebug("Следующий автоскан через {Delay}.", delay);

                if (!await WaitForDelayAsync(delay, settings, stoppingToken).ConfigureAwait(false))
                {
                    // Настройки изменились — пересчитываем интервал без перезапуска.
                    continue;
                }

                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                _logger.LogInformation(
                    "Автоскан запущен: интервал {IntervalMinutes} мин, пауза между запросами {DelayMs} мс.",
                    settings.ScanIntervalMinutes,
                    settings.RequestDelayMs);

                ScanStatus status = await _coordinator
                    .RunAsync(ScanTrigger.Auto, stoppingToken)
                    .ConfigureAwait(false);

                if (status.IsRunning)
                {
                    _logger.LogInformation("Автоскан пропущен: уже идёт другой скан.");
                    await _coordinator.WaitForIdleAsync(stoppingToken).ConfigureAwait(false);
                    continue;
                }

                if (status.Status == ScanRunState.Failed)
                {
                    _logger.LogWarning(
                        "Автоскан завершился с ошибкой: всего {Total}, обновлено {Changed}, ошибок {Failed}. {Message}",
                        status.TorrentsTotal,
                        status.ChangedCount,
                        status.FailedCount,
                        status.Message);
                }
                else
                {
                    _logger.LogInformation(
                        "Автоскан завершён: всего {Total}, обновлено {Changed}, ошибок {Failed}.",
                        status.TorrentsTotal,
                        status.ChangedCount,
                        status.FailedCount);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Штатная остановка приложения.
        }
        finally
        {
            _logger.LogInformation("Планировщик автоскана остановлен.");
        }
    }

    /// <summary>Ожидание до следующего запуска; <c>false</c> — настройки изменились.</summary>
    private async Task<bool> WaitForDelayAsync(
        TimeSpan delay,
        AppSettingsSnapshot expectedSettings,
        CancellationToken stoppingToken)
    {
        if (delay <= TimeSpan.Zero)
        {
            return true;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        Action<AppSettingsSnapshot> onChanged = snapshot =>
        {
            _logger.LogDebug(
                "Настройки изменились: интервал {IntervalMinutes} мин, автоскан {AutoScanEnabled}.",
                snapshot.ScanIntervalMinutes,
                snapshot.AutoScanEnabled);
            linked.Cancel();
        };

        _settingsProvider.Changed += onChanged;

        try
        {
            // Настройки могли измениться между чтением и подпиской — тогда ожидание уже неактуально.
            if (_settingsProvider.Current != expectedSettings)
            {
                _logger.LogDebug("Настройки изменились до подписки, пересчитываю интервал.");
                return false;
            }

            await Task.Delay(delay, _timeProvider, linked.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogDebug("Настройки изменились во время ожидания {Delay}, пересчитываю.", delay);
            return false;
        }
        finally
        {
            _settingsProvider.Changed -= onChanged;
        }
    }

    /// <summary>Ждёт включения автоскана; смена интервала здесь не нужна.</summary>
    private async Task WaitWhileAutoScanDisabledAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_settingsProvider.Current.AutoScanEnabled)
            {
                return;
            }

            var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Action<AppSettingsSnapshot> onChanged = _ => changed.TrySetResult();

            _settingsProvider.Changed += onChanged;

            try
            {
                // Проверяем ещё раз: настройку могли включить между чтением и подпиской.
                if (_settingsProvider.Current.AutoScanEnabled)
                {
                    return;
                }

                using CancellationTokenRegistration registration =
                    stoppingToken.Register(() => changed.TrySetCanceled(stoppingToken));

                await changed.Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            finally
            {
                _settingsProvider.Changed -= onChanged;
            }
        }
    }

    private static TimeSpan ComputeDelay(AppSettingsSnapshot settings, DateTime nowUtc)
    {
        if (settings.LastScanAtUtc is not DateTime lastScanAtUtc)
        {
            // После первого старта LastScanAtUtc пуст — сканируем сразу.
            return TimeSpan.Zero;
        }

        DateTime dueAtUtc = lastScanAtUtc.AddMinutes(settings.ScanIntervalMinutes);
        TimeSpan delay = dueAtUtc - nowUtc;

        return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
    }
}
