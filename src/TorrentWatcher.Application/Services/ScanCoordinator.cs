using Microsoft.Extensions.DependencyInjection;
using TorrentWatcher.Application.Abstractions;
using TorrentWatcher.Application.Dtos;
using TorrentWatcher.Application.Exceptions;
using TorrentWatcher.Application.State;
using TorrentWatcher.Domain.Entities;

namespace TorrentWatcher.Application.Services;

/// <summary>
/// Единственная точка запуска сканов: авто- и ручной запуск не пересекаются
/// (<see cref="SemaphoreSlim"/>), повторный check-all возвращает уже идущий run.
/// Счётчики и статус доступны через singleton <see cref="ScanStatusStore"/>.
/// </summary>
public sealed class ScanCoordinator : IDisposable
{
    /// <summary>Сколько подряд «403/не .torrent» считаем отказом куки, а не сбоем топика.</summary>
    public const int StopAfterConsecutiveFailures = 2;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISettingsProvider _settingsProvider;
    private readonly ScanStatusStore _statusStore;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(initialCount: 1, maxCount: 1);

    public ScanCoordinator(
        IServiceScopeFactory scopeFactory,
        ISettingsProvider settingsProvider,
        ScanStatusStore statusStore,
        TimeProvider timeProvider)
    {
        _scopeFactory = scopeFactory;
        _settingsProvider = settingsProvider;
        _statusStore = statusStore;
        _timeProvider = timeProvider;
    }

    /// <summary>Текущий или последний запуск; <c>null</c> сразу после старта приложения.</summary>
    public ScanStatus? Current => _statusStore.Current;

    /// <summary>
    /// Запускает скан в фоне и сразу возвращает статус. Если скан уже идёт,
    /// новый не стартует: возвращается текущий run с <c>Started = false</c>.
    /// </summary>
    public ScanStartResult StartBackground(ScanTrigger trigger)
    {
        if (!_gate.Wait(0))
        {
            ScanStatus running = _statusStore.Current
                ?? throw new InvalidOperationException("Скан уже идёт, но его статус недоступен.");

            return new ScanStartResult(running, Started: false);
        }

        ScanStatus run = CreateRun(trigger);
        _statusStore.Set(run);

        _ = Task.Run(() => ExecuteAndReleaseAsync(run), CancellationToken.None);

        return new ScanStartResult(run, Started: true);
    }

    /// <summary>
    /// Выполняет скан и дожидается его окончания (используется планировщиком).
    /// Если скан уже идёт, возвращает текущий статус без запуска второго.
    /// </summary>
    public async Task<ScanStatus> RunAsync(ScanTrigger trigger, CancellationToken cancellationToken)
    {
        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return _statusStore.Current
                ?? throw new InvalidOperationException("Скан уже идёт, но его статус недоступен.");
        }

        try
        {
            ScanStatus run = CreateRun(trigger);
            _statusStore.Set(run);

            return await ExecuteAsync(run, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    /// <summary>Ждёт, пока текущий скан (если он идёт) завершится.</summary>
    public async Task WaitForIdleAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        _gate.Release();
    }

    private async Task ExecuteAndReleaseAsync(ScanStatus run)
    {
        try
        {
            await ExecuteAsync(run, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ScanStatus> ExecuteAsync(ScanStatus run, CancellationToken cancellationToken)
    {
        int total = 0;
        int changed = 0;
        int failed = 0;
        int consecutiveStopFailures = 0;
        string? message = null;
        ScanRunState finalState = ScanRunState.Completed;

        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IServiceProvider services = scope.ServiceProvider;

            var repository = services.GetRequiredService<ITrackedTorrentRepository>();
            var scanService = services.GetRequiredService<TorrentScanService>();
            var settingsService = services.GetRequiredService<SettingsService>();

            IReadOnlyList<TrackedTorrent> torrents = await repository
                .GetEnabledAsync(cancellationToken)
                .ConfigureAwait(false);

            total = torrents.Count;
            Publish(run, ScanRunState.Running, total, changed, failed, message: null);

            for (int index = 0; index < torrents.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                TorrentCheckResult result;
                try
                {
                    result = await scanService
                        .CheckAsync(torrents[index].Id, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (AppException)
                {
                    // Торрент удалили между выборкой и проверкой — считаем сбоем и идём дальше.
                    failed++;
                    Publish(run, ScanRunState.Running, total, changed, failed, message: null);
                    continue;
                }

                if (result.Changed)
                {
                    changed++;
                }

                if (!result.Success)
                {
                    failed++;
                }

                Publish(run, ScanRunState.Running, total, changed, failed, message: null);

                if (result.ShouldStopScan)
                {
                    consecutiveStopFailures++;

                    if (consecutiveStopFailures >= StopAfterConsecutiveFailures)
                    {
                        finalState = ScanRunState.Failed;
                        message = "Скан прерван: рутрекер не принимает куку или отдаёт не .torrent.";

                        await TryInvalidateCookieAsync(
                            settingsService,
                            result.Torrent.LastError ?? message,
                            cancellationToken).ConfigureAwait(false);

                        break;
                    }
                }
                else
                {
                    consecutiveStopFailures = 0;
                }

                if (index < torrents.Count - 1)
                {
                    int delayMs = Math.Max(0, _settingsProvider.Current.RequestDelayMs);
                    if (delayMs > 0)
                    {
                        await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            finalState = ScanRunState.Failed;
            message = "Скан отменён.";
        }
        catch (Exception exception)
        {
            finalState = ScanRunState.Failed;
            message = "Скан прерван: " + exception.Message;
        }

        DateTime finishedAt = UtcNow();
        ScanStatus completed = Publish(
            run,
            finalState,
            total,
            changed,
            failed,
            message,
            finishedAt);

        await TryRecordScanFinishedAsync(finishedAt).ConfigureAwait(false);

        return completed;
    }

    private ScanStatus Publish(
        ScanStatus run,
        ScanRunState state,
        int total,
        int changed,
        int failed,
        string? message,
        DateTime? finishedAt = null)
    {
        ScanStatus status = run with
        {
            Status = state,
            TorrentsTotal = total,
            ChangedCount = changed,
            FailedCount = failed,
            Message = message,
            FinishedAtUtc = finishedAt,
        };

        _statusStore.Set(status);
        return status;
    }

    private async Task TryInvalidateCookieAsync(
        SettingsService settingsService,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            await settingsService
                .InvalidateCookieAsync(message, UtcNow(), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Ошибка записи настроек не должна менять итог скана: он уже остановлен.
        }
    }

    private async Task TryRecordScanFinishedAsync(DateTime finishedAtUtc)
    {
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            var settingsService = scope.ServiceProvider.GetRequiredService<SettingsService>();

            await settingsService
                .RecordScanFinishedAsync(finishedAtUtc, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            // LastScanAtUtc влияет только на расчёт следующего запуска; статус скана важнее.
        }
    }

    private ScanStatus CreateRun(ScanTrigger trigger) => new(
        RunId: Guid.NewGuid(),
        Trigger: trigger,
        StartedAtUtc: UtcNow(),
        FinishedAtUtc: null,
        Status: ScanRunState.Running,
        TorrentsTotal: 0,
        ChangedCount: 0,
        FailedCount: 0,
        Message: null);

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}
