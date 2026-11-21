using TorrentWatcher.Application.Abstractions;
using TorrentWatcher.Application.Dtos;
using TorrentWatcher.Application.Exceptions;
using TorrentWatcher.Application.Rutracker;
using TorrentWatcher.Application.Settings;
using TorrentWatcher.Domain.Entities;

namespace TorrentWatcher.Application.Services;

/// <summary>
/// Чтение и сохранение настроек, пробная проверка сеанса рутрекера. Источник истины —
/// строка <see cref="AppSetting"/>; после записи снапшот в <see cref="ISettingsProvider"/>
/// обновляется сразу, поэтому перезапуск не нужен.
/// </summary>
public sealed class SettingsService
{
    private const int MaxErrorMessageLength = 4000;
    private const int MaxCloudflareCookieLength = 4000;
    private const int MaxUserAgentLength = 512;

    private readonly IAppSettingsRepository _settingsRepository;
    private readonly ITrackedTorrentRepository _torrentRepository;
    private readonly IRutrackerClient _rutrackerClient;
    private readonly ITorrentClient _torrentClient;
    private readonly IRutrackerSessionProvider _session;
    private readonly ISecretProtector _protector;
    private readonly ISettingsProvider _provider;
    private readonly SettingsWriteGate _writeGate;
    private readonly TimeProvider _timeProvider;

    public SettingsService(
        IAppSettingsRepository settingsRepository,
        ITrackedTorrentRepository torrentRepository,
        IRutrackerClient rutrackerClient,
        ITorrentClient torrentClient,
        IRutrackerSessionProvider session,
        ISecretProtector protector,
        ISettingsProvider provider,
        SettingsWriteGate writeGate,
        TimeProvider timeProvider)
    {
        _settingsRepository = settingsRepository;
        _torrentRepository = torrentRepository;
        _rutrackerClient = rutrackerClient;
        _torrentClient = torrentClient;
        _session = session;
        _protector = protector;
        _provider = provider;
        _writeGate = writeGate;
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

    /// <summary>Текущие настройки для UI; пароль не возвращается, кука — только маской.</summary>
    public SettingsView Get() => SettingsView.FromSnapshot(_provider.Current);

    /// <summary>Сохраняет настройки и сразу применяет их. Валидирует диапазоны.</summary>
    /// <exception cref="AppException">Некорректные значения (код <c>INVALID_SETTINGS</c>).</exception>
    public async Task<SettingsView> UpdateAsync(SettingsUpdate update, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        Validate(update);

        DateTime now = UtcNow();

        using IDisposable lease = await _writeGate.EnterAsync(cancellationToken).ConfigureAwait(false);

        AppSetting setting = await GetOrCreateAsync(cancellationToken);

        // Смена учётных данных обесценивает прежний сеанс: он принадлежит другому аккаунту
        // или паролю. Кэш куки после этого заполнится первым же входом.
        if (ApplyCredentials(setting, update))
        {
            setting.RutrackerCookie = string.Empty;
            setting.CookieVerifiedAtUtc = null;
            setting.CookieLastError = null;
            _session.Reset();
        }

        ApplyCookie(setting, update);

        setting.ScanIntervalMinutes = update.ScanIntervalMinutes;
        setting.AutoScanEnabled = update.AutoScanEnabled;
        setting.RequestDelayMs = update.RequestDelayMs;
        setting.RequestTimeoutSeconds = update.RequestTimeoutSeconds;
        ApplyCloudflare(setting, update, now);
        ApplyQbittorrent(setting, update);
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

        IReadOnlyList<TrackedTorrent> torrents = await _torrentRepository.GetAllAsync(cancellationToken);
        TrackedTorrent? sample = torrents.Count > 0 ? torrents[0] : null;

        if (sample is null)
        {
            return new CookieTestResult(
                false,
                now,
                "Нет отслеживаемых торрентов: добавьте хотя бы один, чтобы проверить сеанс.");
        }

        TimeSpan timeout = setting.RequestTimeoutSeconds > 0
            ? TimeSpan.FromSeconds(setting.RequestTimeoutSeconds)
            : TimeSpan.FromSeconds(AppSetting.DefaultRequestTimeoutSeconds);

        try
        {
            // Если кэша куки нет, сеанс выполнит вход по сохранённым логину и паролю.
            string cookie = await _session.GetCookieAsync(cancellationToken);

            await _rutrackerClient.DownloadTorrentAsync(
                new RutrackerDownloadRequest(sample.TopicId, cookie, timeout),
                cancellationToken);

            // Пока шли сетевые вызовы, настройки могли измениться: перечитываем под блокировкой.
            using (await _writeGate.EnterAsync(cancellationToken).ConfigureAwait(false))
            {
                setting = await GetOrCreateAsync(cancellationToken);
                setting.CookieVerifiedAtUtc = now;
                setting.CookieLastError = null;
                setting.UpdatedAtUtc = now;
                await _settingsRepository.SaveAsync(setting, cancellationToken);
                _provider.Apply(AppSettingsSnapshot.FromEntity(setting));
            }

            return new CookieTestResult(
                true,
                now,
                $"Сеанс действителен: скачан .torrent топика {sample.TopicId}.");
        }
        catch (RutrackerException exception)
        {
            using (await _writeGate.EnterAsync(cancellationToken).ConfigureAwait(false))
            {
                setting = await GetOrCreateAsync(cancellationToken);

                // Отклонённую куку выбрасываем: при сохранённых логине и пароле следующий
                // запрос выполнит новый вход, а не будет повторять мёртвый сеанс.
                if (exception.Kind == RutrackerFailureKind.CookieInvalid)
                {
                    setting.RutrackerCookie = string.Empty;
                }

                setting.CookieVerifiedAtUtc = null;
                setting.CookieLastError = Truncate(exception.Message);
                setting.UpdatedAtUtc = now;
                await _settingsRepository.SaveAsync(setting, cancellationToken);
                _provider.Apply(AppSettingsSnapshot.FromEntity(setting));
            }

            return new CookieTestResult(false, now, exception.Message);
        }
    }

    /// <summary>
    /// Проверяет соединение с qBittorrent по сохранённым настройкам. Результат сохраняется
    /// в строке настроек, поэтому UI сразу покажет версию клиента или текст ошибки.
    /// </summary>
    public async Task<TorrentClientTestResult> TestQbittorrentAsync(CancellationToken cancellationToken)
    {
        DateTime now = UtcNow();

        if (!_provider.Current.QbittorrentConfigured)
        {
            return new TorrentClientTestResult(
                false,
                now,
                "Укажите адрес Web UI, логин и пароль qBittorrent.");
        }

        try
        {
            string version = await _torrentClient.GetVersionAsync(cancellationToken);

            await SaveQbittorrentTestResultAsync(
                verifiedAtUtc: now,
                error: null,
                now,
                cancellationToken);

            return new TorrentClientTestResult(
                true,
                now,
                $"Соединение установлено: qBittorrent {version}.");
        }
        catch (AppException exception)
        {
            await SaveQbittorrentTestResultAsync(
                verifiedAtUtc: null,
                error: Truncate(exception.Message),
                now,
                cancellationToken);

            return new TorrentClientTestResult(false, now, exception.Message);
        }
    }

    private async Task SaveQbittorrentTestResultAsync(
        DateTime? verifiedAtUtc,
        string? error,
        DateTime now,
        CancellationToken cancellationToken)
    {
        using (await _writeGate.EnterAsync(cancellationToken).ConfigureAwait(false))
        {
            AppSetting setting = await GetOrCreateAsync(cancellationToken);

            setting.QbittorrentVerifiedAtUtc = verifiedAtUtc;
            setting.QbittorrentLastError = error;
            setting.UpdatedAtUtc = now;
            await _settingsRepository.SaveAsync(setting, cancellationToken);
            _provider.Apply(AppSettingsSnapshot.FromEntity(setting));
        }
    }

    /// <summary>
    /// Фиксирует завершение скана: от этой даты планировщик считает следующий запуск
    /// после рестарта. Не влияет на интервал и куку.
    /// </summary>
    public async Task RecordScanFinishedAsync(DateTime finishedAtUtc, CancellationToken cancellationToken)
    {
        using (await _writeGate.EnterAsync(cancellationToken).ConfigureAwait(false))
        {
            AppSetting setting = await GetOrCreateAsync(cancellationToken);

            setting.LastScanAtUtc = finishedAtUtc;
            setting.UpdatedAtUtc = finishedAtUtc;
            await _settingsRepository.SaveAsync(setting, cancellationToken);
            _provider.Apply(AppSettingsSnapshot.FromEntity(setting));
        }
    }

    /// <summary>
    /// Помечает сеанс недействительным: выбрасывает кэш куки, сбрасывает подтверждение
    /// и сохраняет причину, чтобы UI показал баннер. Учётные данные при этом остаются:
    /// следующий запрос выполнит новый вход.
    /// </summary>
    public async Task InvalidateCookieAsync(
        string message,
        DateTime failedAtUtc,
        CancellationToken cancellationToken)
    {
        using (await _writeGate.EnterAsync(cancellationToken).ConfigureAwait(false))
        {
            AppSetting setting = await GetOrCreateAsync(cancellationToken);

            setting.RutrackerCookie = string.Empty;
            setting.CookieVerifiedAtUtc = null;
            setting.CookieLastError = Truncate(message);
            setting.UpdatedAtUtc = failedAtUtc;
            await _settingsRepository.SaveAsync(setting, cancellationToken);
            _provider.Apply(AppSettingsSnapshot.FromEntity(setting));
        }
    }

    private async Task<AppSetting> GetOrCreateAsync(CancellationToken cancellationToken)
    {
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

        if (update.CloudflareClearanceCookie?.Length > MaxCloudflareCookieLength)
        {
            throw new AppException(
                ErrorCodes.SettingsInvalid,
                $"Пропуск Cloudflare слишком длинный (лимит {MaxCloudflareCookieLength} символов).");
        }

        if (update.CloudflareUserAgent?.Length > MaxUserAgentLength)
        {
            throw new AppException(
                ErrorCodes.SettingsInvalid,
                $"User-Agent слишком длинный (лимит {MaxUserAgentLength} символов).");
        }

        if (update.RutrackerLogin?.Trim().Length > AppSetting.MaxLoginLength)
        {
            throw new AppException(
                ErrorCodes.SettingsInvalid,
                $"Логин рутрекера слишком длинный (лимит {AppSetting.MaxLoginLength} символов).");
        }

        if (update.RutrackerPassword?.Length > AppSetting.MaxPasswordLength)
        {
            throw new AppException(
                ErrorCodes.SettingsInvalid,
                $"Пароль рутрекера слишком длинный (лимит {AppSetting.MaxPasswordLength} символов).");
        }

        if (update.RutrackerCookie?.Length > MaxCloudflareCookieLength)
        {
            throw new AppException(
                ErrorCodes.SettingsInvalid,
                $"Кука рутрекера слишком длинная (лимит {MaxCloudflareCookieLength} символов).");
        }

        string? qbittorrentUrl = update.QbittorrentUrl?.Trim();
        if (qbittorrentUrl?.Length > AppSetting.MaxUrlLength)
        {
            throw new AppException(
                ErrorCodes.SettingsInvalid,
                $"Адрес qBittorrent слишком длинный (лимит {AppSetting.MaxUrlLength} символов).");
        }

        if (qbittorrentUrl is { Length: > 0 }
            && (!Uri.TryCreate(qbittorrentUrl, UriKind.Absolute, out Uri? qbittorrentUri)
                || (qbittorrentUri.Scheme != Uri.UriSchemeHttp && qbittorrentUri.Scheme != Uri.UriSchemeHttps)))
        {
            throw new AppException(
                ErrorCodes.SettingsInvalid,
                "Адрес qBittorrent должен быть абсолютным http(s)-адресом Web UI, например http://plex.local:8080.");
        }

        if (update.QbittorrentUsername?.Trim().Length > AppSetting.MaxLoginLength)
        {
            throw new AppException(
                ErrorCodes.SettingsInvalid,
                $"Логин qBittorrent слишком длинный (лимит {AppSetting.MaxLoginLength} символов).");
        }

        if (update.QbittorrentPassword?.Length > AppSetting.MaxPasswordLength)
        {
            throw new AppException(
                ErrorCodes.SettingsInvalid,
                $"Пароль qBittorrent слишком длинный (лимит {AppSetting.MaxPasswordLength} символов).");
        }
    }

    /// <summary>
    /// Логин и пароль: <c>null</c> — «не трогать», пустая строка — очистить.
    /// Возвращает <c>true</c>, если учётные данные изменились и старый сеанс обесценился.
    /// </summary>
    private bool ApplyCredentials(AppSetting setting, SettingsUpdate update)
    {
        bool changed = false;

        if (update.RutrackerLogin is not null)
        {
            string login = update.RutrackerLogin.Trim();

            if (!string.Equals(login, setting.RutrackerLogin, StringComparison.Ordinal))
            {
                setting.RutrackerLogin = login;
                changed = true;
            }
        }

        // Пароль без логина бесполезен: очистка логина забывает и его.
        bool clearPassword = update.RutrackerPassword is { Length: 0 }
            || (update.RutrackerLogin is not null && setting.RutrackerLogin.Length == 0);

        if (clearPassword)
        {
            if (setting.RutrackerPasswordProtected.Length > 0)
            {
                setting.RutrackerPasswordProtected = string.Empty;
                changed = true;
            }
        }
        else if (update.RutrackerPassword is not null)
        {
            // Data Protection каждый раз даёт новую строку, поэтому это всегда «новый пароль».
            setting.RutrackerPasswordProtected = _protector.Protect(update.RutrackerPassword);
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Ручной сеанс: <c>null</c> — «не трогать», пустая строка — очистить. Обычно куку
    /// заполняет приложение после входа; поле нужно, если браузер недоступен.
    /// </summary>
    private static void ApplyCookie(AppSetting setting, SettingsUpdate update)
    {
        if (update.RutrackerCookie is null)
        {
            return;
        }

        string cookie = update.RutrackerCookie.Trim();

        if (cookie.Length == 0)
        {
            setting.RutrackerCookie = string.Empty;
            setting.CookieVerifiedAtUtc = null;
            setting.CookieLastError = null;
            return;
        }

        if (!string.Equals(cookie, setting.RutrackerCookie, StringComparison.Ordinal))
        {
            setting.RutrackerCookie = cookie;
            setting.CookieVerifiedAtUtc = null;
            setting.CookieLastError = null;
        }
    }

    /// <summary>
    /// Ручной режим: пользователь вставляет cf_clearance и UA из своего браузера. Пустая
    /// строка очищает пропуск, <c>null</c> — «не трогать». Смена UA без нового пропуска
    /// обесценивает старый: Cloudflare привязывает clearance к User-Agent.
    /// </summary>
    private static void ApplyCloudflare(AppSetting setting, SettingsUpdate update, DateTime now)
    {
        string? requestedUserAgent = update.CloudflareUserAgent is null
            ? setting.CloudflareUserAgent
            : NullIfWhiteSpace(update.CloudflareUserAgent);

        if (update.CloudflareClearanceCookie is not null)
        {
            string cookie = update.CloudflareClearanceCookie.Trim();

            if (cookie.Length == 0)
            {
                setting.CloudflareClearanceCookie = null;
                setting.CloudflareClearanceObtainedAtUtc = null;
                setting.CloudflareClearanceExpiresAtUtc = null;
                setting.CloudflareLastError = null;
                setting.CloudflareUserAgent = requestedUserAgent;
                return;
            }

            setting.CloudflareClearanceCookie = cookie;
            setting.CloudflareUserAgent = requestedUserAgent;
            setting.CloudflareClearanceObtainedAtUtc = now;
            // Срок жизни вставленной вручную куки неизвестен — узнаем по отказу.
            setting.CloudflareClearanceExpiresAtUtc = null;
            setting.CloudflareLastError = null;
            return;
        }

        if (!string.Equals(requestedUserAgent, setting.CloudflareUserAgent, StringComparison.Ordinal))
        {
            setting.CloudflareUserAgent = requestedUserAgent;
            setting.CloudflareClearanceCookie = null;
            setting.CloudflareClearanceObtainedAtUtc = null;
            setting.CloudflareClearanceExpiresAtUtc = null;
        }
    }

    /// <summary>
    /// Настройки qBittorrent: <c>null</c> — «не трогать», пустая строка очищает. Смена адреса
    /// или учётных данных сбрасывает результат прошлой проверки соединения.
    /// </summary>
    private void ApplyQbittorrent(AppSetting setting, SettingsUpdate update)
    {
        bool credentialsChanged = false;

        if (update.QbittorrentUrl is not null)
        {
            string url = update.QbittorrentUrl.Trim().TrimEnd('/');

            if (!string.Equals(url, setting.QbittorrentUrl, StringComparison.Ordinal))
            {
                setting.QbittorrentUrl = url;
                credentialsChanged = true;
            }
        }

        if (update.QbittorrentUsername is not null)
        {
            string username = update.QbittorrentUsername.Trim();

            if (!string.Equals(username, setting.QbittorrentUsername, StringComparison.Ordinal))
            {
                setting.QbittorrentUsername = username;
                credentialsChanged = true;
            }
        }

        // Пароль без логина бесполезен: очистка логина забывает и его.
        bool clearPassword = update.QbittorrentPassword is { Length: 0 }
            || (update.QbittorrentUsername is not null && setting.QbittorrentUsername.Length == 0);

        if (clearPassword)
        {
            if (setting.QbittorrentPasswordProtected.Length > 0)
            {
                setting.QbittorrentPasswordProtected = string.Empty;
                credentialsChanged = true;
            }
        }
        else if (update.QbittorrentPassword is not null)
        {
            setting.QbittorrentPasswordProtected = _protector.Protect(update.QbittorrentPassword);
            credentialsChanged = true;
        }

        if (update.QbittorrentAutoApply is bool autoApply)
        {
            setting.QbittorrentAutoApply = autoApply;
        }

        if (credentialsChanged)
        {
            setting.QbittorrentVerifiedAtUtc = null;
            setting.QbittorrentLastError = null;
        }
    }

    private static string? NullIfWhiteSpace(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private static string Truncate(string value) =>
        value.Length <= MaxErrorMessageLength ? value : value[..MaxErrorMessageLength];
}
