using TorrentWatcher.Application.Abstractions;
using TorrentWatcher.Application.Dtos;
using TorrentWatcher.Application.Exceptions;
using TorrentWatcher.Application.Rutracker;
using TorrentWatcher.Application.Settings;
using TorrentWatcher.Domain.Entities;

namespace TorrentWatcher.Application.Services;

/// <summary>
/// Чтение и сохранение настроек, пробная проверка куки. Источник истины — строка
/// <see cref="AppSetting"/>; после записи снапшот в <see cref="ISettingsProvider"/>
/// обновляется сразу, поэтому перезапуск не нужен.
/// </summary>
public sealed class SettingsService
{
    private const int MaxErrorMessageLength = 4000;

    private readonly IAppSettingsRepository _settingsRepository;
    private readonly ITrackedTorrentRepository _torrentRepository;
    private readonly IRutrackerClient _rutrackerClient;
    private readonly ISettingsProvider _provider;
    private readonly TimeProvider _timeProvider;

    public SettingsService(
        IAppSettingsRepository settingsRepository,
        ITrackedTorrentRepository torrentRepository,
        IRutrackerClient rutrackerClient,
        ISettingsProvider provider,
        TimeProvider timeProvider)
    {
        _settingsRepository = settingsRepository;
        _torrentRepository = torrentRepository;
        _rutrackerClient = rutrackerClient;
        _provider = provider;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Загружает настройки в снапшот при старте. Если строки нет (миграция не засеяла),
    /// создаёт её со значениями по умолчанию.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        AppSetting setting = await GetOrCreateAsync(cancellationToken);
        _provider.Apply(AppSettingsSnapshot.FromEntity(setting));
    }

    /// <summary>Текущие настройки для UI; кука возвращается только маской.</summary>
    public SettingsView Get() => SettingsView.FromSnapshot(_provider.Current);

    /// <summary>Сохраняет настройки и сразу применяет их. Валидирует диапазоны.</summary>
    /// <exception cref="AppException">Некорректные значения (код <c>INVALID_SETTINGS</c>).</exception>
    public async Task<SettingsView> UpdateAsync(SettingsUpdate update, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        Validate(update);

        DateTime now = UtcNow();
        AppSetting setting = await GetOrCreateAsync(cancellationToken);

        // null — «куку не трогать»: UI не присылает сохранённое значение обратно.
        string? cookie = update.RutrackerCookie?.Trim();
        if (cookie is not null && !string.Equals(cookie, setting.RutrackerCookie, StringComparison.Ordinal))
        {
            setting.RutrackerCookie = cookie;
            setting.CookieVerifiedAtUtc = null;
            setting.CookieLastError = null;
        }

        setting.ScanIntervalMinutes = update.ScanIntervalMinutes;
        setting.AutoScanEnabled = update.AutoScanEnabled;
        setting.RequestDelayMs = update.RequestDelayMs;
        setting.RequestTimeoutSeconds = update.RequestTimeoutSeconds;
        setting.UpdatedAtUtc = now;

        await _settingsRepository.SaveAsync(setting, cancellationToken);
        _provider.Apply(AppSettingsSnapshot.FromEntity(setting));

        return Get();
    }

    /// <summary>
    /// Пробует скачать .torrent по первому отслеживаемому топику с текущей кукой.
    /// Результат (включая отказ рутрекера) возвращается как данные, а не исключение.
    /// </summary>
    public async Task<CookieTestResult> TestCookieAsync(CancellationToken cancellationToken)
    {
        DateTime now = UtcNow();
        AppSetting setting = await GetOrCreateAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(setting.RutrackerCookie))
        {
            return new CookieTestResult(false, now, "Кука рутрекера не задана.");
        }

        IReadOnlyList<TrackedTorrent> torrents = await _torrentRepository.GetAllAsync(cancellationToken);
        TrackedTorrent? sample = torrents.Count > 0 ? torrents[0] : null;

        if (sample is null)
        {
            return new CookieTestResult(
                false,
                now,
                "Нет отслеживаемых торрентов: добавьте хотя бы один, чтобы проверить куку.");
        }

        TimeSpan timeout = setting.RequestTimeoutSeconds > 0
            ? TimeSpan.FromSeconds(setting.RequestTimeoutSeconds)
            : TimeSpan.FromSeconds(AppSetting.DefaultRequestTimeoutSeconds);

        try
        {
            await _rutrackerClient.DownloadTorrentAsync(
                new RutrackerDownloadRequest(sample.TopicId, setting.RutrackerCookie, timeout),
                cancellationToken);

            setting.CookieVerifiedAtUtc = now;
            setting.CookieLastError = null;
            setting.UpdatedAtUtc = now;
            await _settingsRepository.SaveAsync(setting, cancellationToken);
            _provider.Apply(AppSettingsSnapshot.FromEntity(setting));

            return new CookieTestResult(
                true,
                now,
                $"Кука действительна: скачан .torrent топика {sample.TopicId}.");
        }
        catch (RutrackerException exception)
        {
            setting.CookieLastError = Truncate(exception.Message);
            setting.UpdatedAtUtc = now;
            await _settingsRepository.SaveAsync(setting, cancellationToken);
            _provider.Apply(AppSettingsSnapshot.FromEntity(setting));

            return new CookieTestResult(false, now, exception.Message);
        }
    }

    /// <summary>
    /// Фиксирует завершение скана: от этой даты планировщик считает следующий запуск
    /// после рестарта. Не влияет на интервал и куку.
    /// </summary>
    public async Task RecordScanFinishedAsync(DateTime finishedAtUtc, CancellationToken cancellationToken)
    {
        AppSetting setting = await GetOrCreateAsync(cancellationToken);

        setting.LastScanAtUtc = finishedAtUtc;
        setting.UpdatedAtUtc = finishedAtUtc;
        await _settingsRepository.SaveAsync(setting, cancellationToken);
        _provider.Apply(AppSettingsSnapshot.FromEntity(setting));
    }

    /// <summary>
    /// Помечает куку недействительной: сбрасывает подтверждение и сохраняет причину,
    /// чтобы UI показал баннер, не дожидаясь ручной проверки.
    /// </summary>
    public async Task InvalidateCookieAsync(
        string message,
        DateTime failedAtUtc,
        CancellationToken cancellationToken)
    {
        AppSetting setting = await GetOrCreateAsync(cancellationToken);

        setting.CookieVerifiedAtUtc = null;
        setting.CookieLastError = Truncate(message);
        setting.UpdatedAtUtc = failedAtUtc;
        await _settingsRepository.SaveAsync(setting, cancellationToken);
        _provider.Apply(AppSettingsSnapshot.FromEntity(setting));
    }

    private async Task<AppSetting> GetOrCreateAsync(CancellationToken cancellationToken)    {
        AppSetting? setting = await _settingsRepository.FindAsync(cancellationToken);
        if (setting is not null)
        {
            return setting;
        }

        setting = new AppSetting { UpdatedAtUtc = UtcNow() };
        await _settingsRepository.SaveAsync(setting, cancellationToken);

        return setting;
    }

    private static void Validate(SettingsUpdate update)
    {
        if (update.ScanIntervalMinutes is < AppSetting.MinScanIntervalMinutes
            or > AppSetting.MaxScanIntervalMinutes)
        {
            throw new AppException(
                ErrorCodes.SettingsInvalid,
                $"Интервал автоскана должен быть от {AppSetting.MinScanIntervalMinutes} "
                + $"до {AppSetting.MaxScanIntervalMinutes} минут.");
        }

        if (update.RequestDelayMs < 0)
        {
            throw new AppException(
                ErrorCodes.SettingsInvalid,
                "Задержка между запросами не может быть отрицательной.");
        }

        if (update.RequestTimeoutSeconds <= 0)
        {
            throw new AppException(
                ErrorCodes.SettingsInvalid,
                "Таймаут запроса должен быть положительным.");
        }
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private static string Truncate(string value) =>
        value.Length <= MaxErrorMessageLength ? value : value[..MaxErrorMessageLength];
}
