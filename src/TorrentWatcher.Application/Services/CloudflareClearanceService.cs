using Microsoft.Extensions.Logging;
using TorrentWatcher.Application.Abstractions;
using TorrentWatcher.Application.Dtos;
using TorrentWatcher.Application.Exceptions;
using TorrentWatcher.Application.Rutracker;
using TorrentWatcher.Application.Settings;
using TorrentWatcher.Domain.Entities;

namespace TorrentWatcher.Application.Services;

/// <summary>
/// Хранит пропуск Cloudflare в настройках и обновляет снапшот сразу после решения челленджа.
/// Кука не логируется: в журнал попадает только факт получения/сброса и срок действия.
/// </summary>
public sealed class CloudflareClearanceService : ICloudflareClearanceProvider
{
    private const int MaxErrorMessageLength = 4000;

    private readonly IAppSettingsRepository _settingsRepository;
    private readonly ITrackedTorrentRepository _torrentRepository;
    private readonly ICloudflareChallengeSolver _solver;
    private readonly ISettingsProvider _provider;
    private readonly SettingsWriteGate _writeGate;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CloudflareClearanceService> _logger;

    public CloudflareClearanceService(
        IAppSettingsRepository settingsRepository,
        ITrackedTorrentRepository torrentRepository,
        ICloudflareChallengeSolver solver,
        ISettingsProvider provider,
        SettingsWriteGate writeGate,
        TimeProvider timeProvider,
        ILogger<CloudflareClearanceService> logger)
    {
        _settingsRepository = settingsRepository;
        _torrentRepository = torrentRepository;
        _solver = solver;
        _provider = provider;
        _writeGate = writeGate;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public CloudflareClearance? Current
    {
        get
        {
            AppSettingsSnapshot settings = _provider.Current;

            if (string.IsNullOrWhiteSpace(settings.CloudflareClearanceCookie))
            {
                return null;
            }

            var clearance = new CloudflareClearance(
                settings.CloudflareClearanceCookie,
                string.IsNullOrWhiteSpace(settings.CloudflareUserAgent)
                    ? RutrackerDefaults.UserAgent
                    : settings.CloudflareUserAgent,
                settings.CloudflareClearanceObtainedAtUtc ?? DateTime.MinValue,
                settings.CloudflareClearanceExpiresAtUtc);

            return clearance.IsExpired(UtcNow()) ? null : clearance;
        }
    }

    public async Task<CloudflareClearance> SolveAsync(int topicId, CancellationToken cancellationToken)
    {
        CloudflareClearance clearance = await _solver
            .SolveAsync(topicId, cancellationToken)
            .ConfigureAwait(false);

        await SaveAsync(clearance, cancellationToken).ConfigureAwait(false);
        return clearance;
    }

    public async Task SaveAsync(CloudflareClearance clearance, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(clearance);

        using (await _writeGate.EnterAsync(cancellationToken).ConfigureAwait(false))
        {
            AppSetting setting = await GetOrCreateAsync(cancellationToken).ConfigureAwait(false);
            setting.CloudflareClearanceCookie = clearance.Cookie;
            setting.CloudflareUserAgent = clearance.UserAgent;
            setting.CloudflareClearanceObtainedAtUtc = clearance.ObtainedAtUtc;
            setting.CloudflareClearanceExpiresAtUtc = clearance.ExpiresAtUtc;
            setting.CloudflareLastError = null;
            setting.UpdatedAtUtc = UtcNow();

            await _settingsRepository.SaveAsync(setting, cancellationToken).ConfigureAwait(false);
            _provider.Apply(AppSettingsSnapshot.FromEntity(setting));
        }

        _logger.LogInformation(
            "Пропуск Cloudflare сохранён. Действует до {ExpiresAtUtc}.",
            clearance.ExpiresAtUtc);
    }

    public async Task ReportRejectedAsync(
        string message,
        DateTime failedAtUtc,
        CancellationToken cancellationToken)
    {
        using (await _writeGate.EnterAsync(cancellationToken).ConfigureAwait(false))
        {
            AppSetting setting = await GetOrCreateAsync(cancellationToken).ConfigureAwait(false);
            setting.CloudflareClearanceCookie = null;
            setting.CloudflareClearanceObtainedAtUtc = null;
            setting.CloudflareClearanceExpiresAtUtc = null;
            setting.CloudflareLastError = Truncate(message);
            setting.UpdatedAtUtc = failedAtUtc;

            await _settingsRepository.SaveAsync(setting, cancellationToken).ConfigureAwait(false);
            _provider.Apply(AppSettingsSnapshot.FromEntity(setting));
        }

        _logger.LogWarning("Пропуск Cloudflare сброшен: повторный челлендж после решения.");
    }

    /// <summary>
    /// Решение челленджа по кнопке в UI. Топик не важен: подойдёт любой, у которого
    /// <c>dl.php</c> закрыт Cloudflare; при пустом списке берём топик 1.
    /// </summary>
    public async Task<CloudflareSolveResult> SolveForUiAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<TrackedTorrent> torrents = await _torrentRepository
            .GetAllAsync(cancellationToken)
            .ConfigureAwait(false);

        int topicId = torrents.Count > 0 ? torrents[0].TopicId : 1;

        try
        {
            CloudflareClearance clearance = await SolveAsync(topicId, cancellationToken)
                .ConfigureAwait(false);

            return new CloudflareSolveResult(
                Success: true,
                Message: "Проверка Cloudflare пройдена, пропуск сохранён.",
                ObtainedAtUtc: clearance.ObtainedAtUtc,
                ExpiresAtUtc: clearance.ExpiresAtUtc);
        }
        catch (RutrackerException exception)
        {
            return new CloudflareSolveResult(
                Success: false,
                Message: exception.Message,
                ObtainedAtUtc: null,
                ExpiresAtUtc: null);
        }
    }

    private async Task<AppSetting> GetOrCreateAsync(CancellationToken cancellationToken)
    {
        AppSetting? setting = await _settingsRepository
            .FindAsync(cancellationToken)
            .ConfigureAwait(false);

        return setting ?? new AppSetting { UpdatedAtUtc = UtcNow() };
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private static string Truncate(string value) =>
        value.Length <= MaxErrorMessageLength ? value : value[..MaxErrorMessageLength];
}
