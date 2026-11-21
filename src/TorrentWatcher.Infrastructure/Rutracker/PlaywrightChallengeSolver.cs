using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using TorrentWatcher.Application.Abstractions;
using TorrentWatcher.Application.Exceptions;
using TorrentWatcher.Application.Rutracker;
using TorrentWatcher.Domain;

namespace TorrentWatcher.Infrastructure.Rutracker;

/// <summary>
/// Проходит managed challenge Cloudflare настоящим Chromium (Playwright) и забирает
/// <c>cf_clearance</c> вместе с User-Agent. Браузер запускается один раз с persistent-профилем
/// и переиспользуется; все запросы к нему сериализованы. Куки в журнал не попадают.
/// </summary>
public sealed class PlaywrightChallengeSolver
    : ICloudflareChallengeSolver, IBrowserTorrentDownloader, IRutrackerLogin, IAsyncDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Адреса для чтения кук. Рутрекер ставит <c>bb_*</c> с путём <c>/forum/</c>, поэтому
    /// в списке нужен именно адрес с завершающим слешем: без него фильтр по URL куки не видит.
    /// </summary>
    private static readonly string[] CookieUrls =
    [
        RutrackerUrls.BaseUrl + "/",
        RutrackerUrls.ForumUrl + "/",
    ];

    /// <summary>
    /// Таймаут одной пробы состояния страницы. Опрос идёт раз в секунду, поэтому проба
    /// не должна ждать элемента дольше: иначе цикл залипает, а страница успевает уйти
    /// на следующую (например, после успешного входа форма исчезает).
    /// </summary>
    private const float ProbeTimeoutMs = 2000f;

    /// <summary>Страховка от таймаутов Playwright по умолчанию (30 с) вне опроса.</summary>
    private const float DefaultPageTimeoutMs = 15000f;

    private readonly RutrackerBrowserOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PlaywrightChallengeSolver> _logger;
    private readonly SemaphoreSlim _gate = new(initialCount: 1, maxCount: 1);

    private IPlaywright? _playwright;
    private IBrowserContext? _context;
    private bool _disposed;

    public PlaywrightChallengeSolver(
        RutrackerBrowserOptions options,
        TimeProvider timeProvider,
        ILogger<PlaywrightChallengeSolver> logger)
    {
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<CloudflareClearance> SolveAsync(int topicId, CancellationToken cancellationToken)
    {
        // Топик нужен только чтобы попасть на защищённый dl.php: подойдёт любой.
        int id = topicId > 0 ? topicId : 1;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            IBrowserContext context = await GetContextAsync(cancellationToken).ConfigureAwait(false);
            IPage page = await context.NewPageAsync().ConfigureAwait(false);

            try
            {
                DateTime deadline = UtcNow().Add(_options.SolveTimeout);
                string url = RutrackerUrls.Download(id);

                _logger.LogInformation(
                    "Прохожу проверку Cloudflare в браузере для dl.php?t={TopicId}.", id);

                // Если в профиле уже есть кука рутрекера, dl.php сразу отдаёт файл,
                // и навигация прерывается сообщением «Download is starting». Это не ошибка:
                // пропуск Cloudflare при этом уже действует.
                bool downloading = false;

                try
                {
                    await page.GotoAsync(
                        url,
                        new PageGotoOptions
                        {
                            WaitUntil = WaitUntilState.DOMContentLoaded,
                            Timeout = (float)_options.SolveTimeout.TotalMilliseconds,
                        }).ConfigureAwait(false);
                }
                catch (PlaywrightException exception) when (IsDownloadStarting(exception))
                {
                    downloading = true;
                }

                while (UtcNow() < deadline)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Cloudflare после проверки делает POST-навигацию: в этот момент
                    // evaluation и title падают с «Execution context was destroyed».
                    // Это не ошибка — просто пробуем ещё раз на следующей итерации.
                    try
                    {
                        IReadOnlyList<BrowserContextCookiesResult> cookies = await context
                            .CookiesAsync(CookieUrls)
                            .ConfigureAwait(false);

                        string? cookie = BuildClearanceCookie(cookies, out DateTime? expiresAtUtc);

                        if (cookie is not null)
                        {
                            string? title = await TryGetTitleAsync(page).ConfigureAwait(false);
                            string? userAgent = await TryEvaluateAsync(page, "navigator.userAgent")
                                .ConfigureAwait(false);

                            if (userAgent is not null
                                && (downloading
                                    || (title is not null && !IsChallengeTitle(title))))
                            {
                                _logger.LogInformation(
                                    "Проверка Cloudflare пройдена, пропуск действует до {ExpiresAtUtc}.",
                                    expiresAtUtc);

                                return new CloudflareClearance(
                                    cookie,
                                    userAgent,
                                    UtcNow(),
                                    expiresAtUtc);
                            }
                        }
                    }
                    catch (Exception exception) when (IsPlaywrightFailure(exception))
                    {
                        // Страница перезагружается — ждём следующую итерацию.
                    }

                    await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
                }

                throw new RutrackerException(
                    RutrackerFailureKind.Challenge,
                    $"Cloudflare не пропустил браузер за {_options.SolveTimeout.TotalSeconds:0} с. "
                    + "Проверьте сеть и повторите позже.");
            }
            finally
            {
                await CloseQuietlyAsync(page).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (IsPlaywrightFailure(exception))
        {
            _logger.LogWarning(exception, "Не удалось пройти проверку Cloudflare в браузере.");

            throw new RutrackerException(
                RutrackerFailureKind.Challenge,
                "Не удалось пройти проверку Cloudflare в браузере. "
                + "Убедитесь, что установлен Chromium Playwright "
                + "(playwright.ps1 install chromium) и доступен дисплей (Xvfb в контейнере).",
                exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<RutrackerBrowserSession> LoginAsync(
        string login,
        string password,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(login);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            IBrowserContext context = await GetContextAsync(cancellationToken).ConfigureAwait(false);

            // Перед входом убираем прежний сеанс рутрекера: иначе login.php уведёт на index
            // и мы вернём куку старого аккаунта, даже если в настройках уже другой логин.
            // Куки Cloudflare (cf_clearance, __cf_bm) не трогаем — они нужны для страницы входа.
            // Внимание: Name — точное имя, шаблон задаётся только через NameRegex.
            await context
                .ClearCookiesAsync(
                    new BrowserContextClearCookiesOptions
                    {
                        NameRegex = new Regex("^bb_"),
                    })
                .ConfigureAwait(false);

            IPage page = await context.NewPageAsync().ConfigureAwait(false);

            try
            {
                // Вход включает прохождение Cloudflare: навигация и POST могут держаться
                // десятки секунд. Поэтому бюджета запроса мало — берём бюджет решателя
                // Cloudflare плюс запас на форму и ответ рутрекера.
                TimeSpan effective = timeout > TimeSpan.Zero
                    ? timeout
                    : TimeSpan.FromSeconds(60);

                TimeSpan challengeBudget = _options.SolveTimeout + TimeSpan.FromSeconds(30);

                if (challengeBudget > effective)
                {
                    effective = challengeBudget;
                }

                DateTime deadline = UtcNow().Add(effective);

                _logger.LogInformation("Вхожу в рутрекер под пользователем {Login}.", login);

                IResponse? loginPage = null;

                try
                {
                    loginPage = await page.GotoAsync(
                        RutrackerUrls.Login,
                        new PageGotoOptions
                        {
                            WaitUntil = WaitUntilState.DOMContentLoaded,
                            Timeout = (float)effective.TotalMilliseconds,
                        }).ConfigureAwait(false);
                }
                catch (Exception exception) when (IsPlaywrightFailure(exception))
                {
                    // Cloudflare может прервать навигацию; состояние страницы проверим ниже.
                }

                if (loginPage is not null)
                {
                    _logger.LogInformation(
                        "Переход на страницу входа: {Chain}.",
                        await DescribeRedirectsAsync(loginPage).ConfigureAwait(false));
                }

                // Ждём форму входа: Cloudflare может держать интерстишл несколько секунд.
                bool formReady = false;
                string? lastUrl = null;

                while (UtcNow() < deadline)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (await IsLoginFormVisibleAsync(page).ConfigureAwait(false))
                    {
                        formReady = true;
                        break;
                    }

                    // Сеанс мог открыться сам: рутрекер уводит login.php на index, если
                    // в профиле уже есть кука (например, вход выполняли вручную).
                    if (await TryReadSessionCookieAsync(context).ConfigureAwait(false) is { } existing)
                    {
                        _logger.LogInformation(
                            "Рутрекер уже открыл сеанс: форма входа не понадобилась.");
                        return new RutrackerBrowserSession(existing);
                    }

                    string? currentUrl = TryGetUrl(page);

                    if (currentUrl is not null && !string.Equals(currentUrl, lastUrl, StringComparison.Ordinal))
                    {
                        lastUrl = currentUrl;
                        _logger.LogInformation(
                            "Жду страницу входа: {Url} («{Title}»).",
                            currentUrl,
                            await TryGetTitleAsync(page).ConfigureAwait(false));
                    }

                    await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
                }

                if (!formReady)
                {
                    // Бывает, что рутрекер уже открыл сеанс (например, им пользовались
                    // в этом профиле вручную) и login.php сразу уводит на index.
                    if (await TryReadSessionCookieAsync(context).ConfigureAwait(false) is { } existing)
                    {
                        _logger.LogInformation(
                            "Рутрекер уже открыл сеанс: форма входа не понадобилась.");
                        return new RutrackerBrowserSession(existing);
                    }

                    await LogLoginStateAsync(page, context, effective, "страница входа не появилась")
                        .ConfigureAwait(false);

                    throw await CreateLoginExceptionAsync(
                        page,
                        "страница входа не появилась").ConfigureAwait(false);
                }

                if (await HasCaptchaAsync(page).ConfigureAwait(false))
                {
                    throw new RutrackerException(
                        RutrackerFailureKind.LoginCaptcha,
                        "Рутрекер требует капчу при входе. Автоматически её решить нельзя: "
                        + "войдите в браузере вручную или задайте куку в разделе «Дополнительно».");
                }

                double remainingMs = Math.Max(
                    ProbeTimeoutMs,
                    (deadline - UtcNow()).TotalMilliseconds);

                await page
                    .FillAsync(
                        "#login-form-full input[name='login_username']",
                        login,
                        new PageFillOptions { Timeout = (float)remainingMs })
                    .ConfigureAwait(false);
                await page
                    .FillAsync(
                        "#login-form-full input[name='login_password']",
                        password,
                        new PageFillOptions { Timeout = (float)remainingMs })
                    .ConfigureAwait(false);

                await SubmitLoginFormAsync(page).ConfigureAwait(false);

                DateTime nextProgressAt = UtcNow();

                while (UtcNow() < deadline)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string? cookie = await TryReadSessionCookieAsync(context).ConfigureAwait(false);
                    bool formVisible = await IsLoginFormVisibleAsync(page).ConfigureAwait(false);

                    if (UtcNow() >= nextProgressAt)
                    {
                        nextProgressAt = UtcNow().AddSeconds(5);
                        _logger.LogDebug(
                            "Жду завершения входа: url={Url}, форма={Form}, куки={Cookies}, "
                            + "строка={Length}.",
                            TryGetUrl(page),
                            formVisible,
                            await DescribeCookieNamesAsync(context).ConfigureAwait(false),
                            cookie?.Length ?? 0);
                    }

                    if (cookie is not null && !formVisible)
                    {
                        _logger.LogInformation("Вход в рутрекер выполнен.");
                        return new RutrackerBrowserSession(cookie);
                    }

                    if (await HasCaptchaAsync(page).ConfigureAwait(false))
                    {
                        throw new RutrackerException(
                            RutrackerFailureKind.LoginCaptcha,
                            "Рутрекер требует капчу при входе. Автоматически её решить нельзя: "
                            + "войдите в браузере вручную или задайте куку в разделе «Дополнительно».");
                    }

                    // Текст формы читаем только когда она на экране: после успешного входа
                    // страница уходит на index, и ожидание исчезнувшей формы съедало бы
                    // весь бюджет времени впустую.
                    if (formVisible
                        && await TryReadLoginErrorAsync(page).ConfigureAwait(false) is { } error)
                    {
                        throw new RutrackerException(RutrackerFailureKind.LoginFailed, error);
                    }

                    await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
                }

                await LogLoginStateAsync(page, context, effective, "вход не завершился")
                    .ConfigureAwait(false);

                throw await CreateLoginExceptionAsync(
                    page,
                    "вход не завершился за отведённое время").ConfigureAwait(false);
            }
            finally
            {
                await CloseQuietlyAsync(page).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (IsPlaywrightFailure(exception))
        {
            _logger.LogWarning(exception, "Браузер не смог выполнить вход в рутрекер.");

            throw new RutrackerException(
                RutrackerFailureKind.LoginFailed,
                "Не удалось выполнить вход в рутрекер в браузере. "
                + "Убедитесь, что установлен Chromium Playwright и доступен дисплей (Xvfb в контейнере).",
                exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BrowserTorrentDownloadResult> DownloadTorrentAsync(
        int topicId,
        string rutrackerCookie,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topicId);

        if (string.IsNullOrWhiteSpace(rutrackerCookie))
        {
            throw new RutrackerException(
                RutrackerFailureKind.CookieInvalid,
                "Кука рутрекера не задана: браузер не сможет скачать .torrent.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            IBrowserContext context = await GetContextAsync(cancellationToken).ConfigureAwait(false);
            await AddRutrackerCookiesAsync(context, rutrackerCookie).ConfigureAwait(false);

            IPage page = await context.NewPageAsync().ConfigureAwait(false);

            try
            {
                // Ждём и челлендж, и скачивание: Cloudflare может думать до SolveTimeout.
                double waitMilliseconds = Math.Max(
                    _options.SolveTimeout.TotalMilliseconds,
                    (timeout > TimeSpan.Zero ? timeout : TimeSpan.FromSeconds(30)).TotalMilliseconds
                        + TimeSpan.FromSeconds(30).TotalMilliseconds);

                string url = RutrackerUrls.Download(topicId);

                Task<IDownload> downloadTask = page.WaitForDownloadAsync(
                    new PageWaitForDownloadOptions { Timeout = (float)waitMilliseconds });

                try
                {
                    await page.GotoAsync(
                        url,
                        new PageGotoOptions
                        {
                            WaitUntil = WaitUntilState.Commit,
                            Timeout = (float)waitMilliseconds,
                        }).ConfigureAwait(false);
                }
                catch (Exception exception) when (IsPlaywrightFailure(exception))
                {
                    // Chrome прерывает навигацию, когда начинает скачивание .torrent.
                }

                IDownload download;
                try
                {
                    download = await downloadTask.ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    throw await CreateNoDownloadExceptionAsync(page, topicId).ConfigureAwait(false);
                }

                string? path = await download.PathAsync().ConfigureAwait(false);
                if (path is null)
                {
                    throw new RutrackerException(
                        RutrackerFailureKind.InvalidPayload,
                        "Браузер не сохранил скачанный .torrent.");
                }

                byte[] payload = await File.ReadAllBytesAsync(path, cancellationToken)
                    .ConfigureAwait(false);

                if (payload.Length > RutrackerClient.MaxTorrentBytes)
                {
                    throw new RutrackerException(
                        RutrackerFailureKind.InvalidPayload,
                        $"Файл .torrent слишком большой: {payload.Length} байт, "
                        + $"лимит — {RutrackerClient.MaxTorrentBytes}.");
                }

                RutrackerClient.EnsureTorrentPayload(payload);

                CloudflareClearance? clearance = await ReadClearanceAsync(context, page)
                    .ConfigureAwait(false);

                _logger.LogInformation(
                    "Файл .torrent для топика {TopicId} скачан браузером ({Bytes} байт).",
                    topicId,
                    payload.Length);

                return new BrowserTorrentDownloadResult(payload, clearance);
            }
            finally
            {
                await CloseQuietlyAsync(page).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (IsPlaywrightFailure(exception))
        {
            _logger.LogWarning(exception, "Браузер не смог скачать .torrent для топика {TopicId}.", topicId);

            throw new RutrackerException(
                RutrackerFailureKind.Challenge,
                "Не удалось скачать .torrent браузером. "
                + "Убедитесь, что Chromium Playwright установлен и доступен дисплей (Xvfb в контейнере).",
                exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_context is not null)
        {
            await _context.CloseAsync().ConfigureAwait(false);
            _context = null;
        }

        _playwright?.Dispose();
        _playwright = null;
        _gate.Dispose();
    }

    private async Task<IBrowserContext> GetContextAsync(CancellationToken cancellationToken)
    {
        if (_context is not null)
        {
            return _context;
        }

        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(_options.ProfileDirectory);

        _playwright ??= await Playwright.CreateAsync().ConfigureAwait(false);

        var launchOptions = new BrowserTypeLaunchPersistentContextOptions
        {
            Headless = _options.Headless,
            Locale = "ru-RU",
            TimezoneId = "Europe/Moscow",
            ViewportSize = new ViewportSize { Width = 1366, Height = 900 },
            // Playwright по умолчанию добавляет --enable-automation; Cloudflare это видит.
            IgnoreDefaultArgs = ["--enable-automation"],
            Args = BuildArgs(),
        };

        if (!string.IsNullOrWhiteSpace(_options.ExecutablePath))
        {
            launchOptions.ExecutablePath = _options.ExecutablePath;
        }

        _context = await LaunchContextAsync(launchOptions).ConfigureAwait(false);

        // Скрываем самый очевидный признак автоматизации. Полноценный stealth не делаем:
        // задача — пройти managed challenge, а не обмануть целенаправленную защиту.
        await _context
            .AddInitScriptAsync("Object.defineProperty(navigator, 'webdriver', { get: () => undefined });")
            .ConfigureAwait(false);

        // Страховка от дефолтного таймаута Playwright в 30 с: длинные шаги
        // (навигация, ожидание скачивания) задают свой таймаут явно.
        _context.SetDefaultTimeout(DefaultPageTimeoutMs);

        return _context;
    }

    /// <summary>
    /// Запускает Chromium с persistent-профилем. Если прошлый процесс не завершился чисто
    /// (контейнер перезапустили, пока браузер работал), в профиле остаётся <c>SingletonLock</c>,
    /// и Chromium отказывается стартовать: «profile appears to be in use». Такой замок
    /// брошен (pid давно мёртв), поэтому снимаем его и пробуем ещё раз — ровно один раз.
    /// </summary>
    private async Task<IBrowserContext> LaunchContextAsync(
        BrowserTypeLaunchPersistentContextOptions launchOptions)
    {
        try
        {
            return await _playwright!.Chromium
                .LaunchPersistentContextAsync(_options.ProfileDirectory, launchOptions)
                .ConfigureAwait(false);
        }
        catch (PlaywrightException exception) when (IsProfileLocked(exception))
        {
            if (!TryClearProfileLock())
            {
                throw;
            }

            _logger.LogWarning(
                "В профиле Chromium остался замок прошлого процесса — снял его "
                + "и запускаю браузер заново.");

            return await _playwright!.Chromium
                .LaunchPersistentContextAsync(_options.ProfileDirectory, launchOptions)
                .ConfigureAwait(false);
        }
    }

    private static bool IsProfileLocked(PlaywrightException exception) =>
        exception.Message.Contains("profile appears to be in use", StringComparison.OrdinalIgnoreCase)
        || exception.Message.Contains("SingletonLock", StringComparison.OrdinalIgnoreCase)
        || exception.Message.Contains("ProcessSingleton", StringComparison.OrdinalIgnoreCase);

    /// <summary>Убирает файлы блокировки профиля. Профилем владеет только это приложение.</summary>
    private bool TryClearProfileLock()
    {
        bool removed = false;

        foreach (string name in new[] { "SingletonLock", "SingletonCookie", "SingletonSocket", "lockfile" })
        {
            string path = Path.Combine(_options.ProfileDirectory, name);

            try
            {
                var lockFile = new FileInfo(path);

                // File.Exists не видит «висячую» символическую ссылку, поэтому проверяем LinkTarget.
                if (lockFile.Exists || lockFile.LinkTarget is not null)
                {
                    lockFile.Delete();
                    removed = true;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(exception, "Не удалось снять замок профиля Chromium: {Path}.", path);
            }
        }

        return removed;
    }

    private string[] BuildArgs()
    {
        var args = new List<string>
        {
            "--disable-blink-features=AutomationControlled",
            "--no-first-run",
            "--no-default-browser-check",
            "--disable-dev-shm-usage",
            "--lang=ru-RU",
        };

        if (_options.DisableSandbox)
        {
            args.Add("--no-sandbox");
        }

        return [.. args];
    }

    private static async Task<bool> IsLoginFormVisibleAsync(IPage page)
    {
        try
        {
            return await page
                .Locator("#login-form-full input[name='login_username']")
                .IsVisibleAsync()
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (IsPlaywrightFailure(exception))
        {
            return false;
        }
    }

    private static async Task<bool> HasCaptchaAsync(IPage page)
    {
        try
        {
            return await page.Locator("input[name='cap_code']").IsVisibleAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (IsPlaywrightFailure(exception))
        {
            return false;
        }
    }

    /// <summary>
    /// Отправляет форму входа. Сначала кликаем по кнопке — это «человечнее» для Cloudflare, —
    /// но с коротким таймаутом: если разметка кнопки изменилась, не тратим весь бюджет
    /// на ожидание селектора, а отправляем форму штатным <c>requestSubmit</c>.
    /// </summary>
    private static async Task SubmitLoginFormAsync(IPage page)
    {
        ILocator submit = page
            .Locator("#login-form-full input[type='submit'], #login-form-full button[type='submit']")
            .First;

        try
        {
            if (await submit.CountAsync().ConfigureAwait(false) > 0)
            {
                await submit
                    .ClickAsync(new LocatorClickOptions { Timeout = ProbeTimeoutMs * 5 })
                    .ConfigureAwait(false);
                return;
            }
        }
        catch (Exception exception) when (IsPlaywrightFailure(exception))
        {
            // Клик мог начать навигацию — состояние страницы проверит цикл ниже.
        }

        await TryEvaluateAsync(
            page,
            "() => { const f = document.getElementById('login-form-full');"
            + " if (!f) return false;"
            + " if (typeof f.requestSubmit === 'function') { f.requestSubmit(); } else { f.submit(); }"
            + " return true; }").ConfigureAwait(false);
    }

    /// <summary>
    /// Снимок состояния страницы, на которой вход так и не завершился. Куки и значения полей
    /// сюда не попадают — только адрес, заголовок и разметка кнопки.
    /// </summary>
    private async Task LogLoginStateAsync(
        IPage page,
        IBrowserContext context,
        TimeSpan budget,
        string reason)
    {
        string? title = await TryGetTitleAsync(page).ConfigureAwait(false);
        bool challenge = await IsChallengePageAsync(page).ConfigureAwait(false);
        bool formVisible = await IsLoginFormVisibleAsync(page).ConfigureAwait(false);
        string? submit = await TryEvaluateAsync(
            page,
            "() => { const b = document.querySelector("
            + "\"#login-form-full input[type='submit'], #login-form-full button\");"
            + " return b ? b.outerHTML : null; }").ConfigureAwait(false);
        string? readyState = await TryEvaluateAsync(page, "() => document.readyState")
            .ConfigureAwait(false);
        string? session = await TryEvaluateAsync(
            page,
            "() => { const u = document.querySelector('#logged-in-username');"
            + " if (!u) return document.querySelector('a[href*=\"login.php\"]') ? 'guest' : 'unknown';"
            + " const box = u.closest('li, div, td') || u;"
            + " return 'есть, видимый=' + (box.offsetParent !== null) + ', текст=' + (u.textContent || '').trim().length; }")
            .ConfigureAwait(false);

        string cookieNames = await DescribeCookieNamesAsync(context).ConfigureAwait(false);

        _logger.LogWarning(
            "Вход в рутрекер не завершился ({Reason}) за {Seconds:0} с: url={Url}, title={Title}, "
            + "челлендж={Challenge}, форма={FormVisible}, readyState={ReadyState}, сеанс={Session}, "
            + "куки={Cookies}, кнопка={Submit}.",
            reason,
            budget.TotalSeconds,
            page.Url,
            title,
            challenge,
            formVisible,
            readyState,
            session,
            cookieNames,
            submit);
    }

    /// <summary>Имена кук рутрекера — без значений, чтобы не попасть в журнал с секретами.</summary>
    private static async Task<string> DescribeCookieNamesAsync(IBrowserContext context)
    {
        try
        {
            IReadOnlyList<BrowserContextCookiesResult> cookies = await context
                .CookiesAsync()
                .ConfigureAwait(false);

            return cookies.Count == 0
                ? "нет"
                : string.Join(
                    ", ",
                    cookies
                        .Where(cookie => cookie.Domain.Contains("rutracker", StringComparison.OrdinalIgnoreCase))
                        .Select(cookie => $"{cookie.Name}@{cookie.Domain}"));
        }
        catch (Exception exception) when (IsPlaywrightFailure(exception))
        {
            return "ошибка чтения";
        }
    }

    /// <summary>
    /// Читает куки сеанса рутрекера из профиля. Все <c>bb_*</c> куки нужны вместе:
    /// <c>bb_session</c> — сам сеанс, <c>bb_data</c> — данные «запомнить меня».
    /// </summary>
    private async Task<string?> TryReadSessionCookieAsync(IBrowserContext context)
    {
        try
        {
            IReadOnlyList<BrowserContextCookiesResult> cookies = await context
                .CookiesAsync(CookieUrls)
                .ConfigureAwait(false);

            bool IsSessionCookie(BrowserContextCookiesResult cookie) =>
                cookie.Name.StartsWith("bb_", StringComparison.Ordinal)
                && !string.IsNullOrEmpty(cookie.Value);

            // bb_guid и bb_ssl рутрекер выдаёт и гостю. Признак входа — именно bb_session.
            bool signedIn = cookies.Any(
                cookie => cookie.Name == "bb_session" && IsSessionCookie(cookie));

            string[] parts = cookies
                .Where(IsSessionCookie)
                .GroupBy(cookie => cookie.Name, StringComparer.Ordinal)
                .Select(group => $"{group.Key}={group.Last().Value}")
                .ToArray();

            _logger.LogDebug(
                "Куки сеанса: {Detail}; собрано {Count}.",
                string.Join(
                    ", ",
                    cookies.Select(cookie => $"{cookie.Name}:{cookie.Value?.Length ?? -1}")),
                parts.Length);

            return signedIn && parts.Length > 0 ? string.Join("; ", parts) : null;
        }
        catch (Exception exception) when (IsPlaywrightFailure(exception))
        {
            _logger.LogWarning(exception, "Не удалось прочитать куки сеанса рутрекера.");
            return null;
        }
    }

    /// <summary>Распознаёт сообщение формы о неверном логине/пароле.</summary>
    private static async Task<string?> TryReadLoginErrorAsync(IPage page)
    {
        try
        {
            string text = await page
                .Locator("#login-form-full")
                .InnerTextAsync(new LocatorInnerTextOptions { Timeout = ProbeTimeoutMs })
                .ConfigureAwait(false);

            bool rejected = text.Contains("неверн", StringComparison.OrdinalIgnoreCase)
                || text.Contains("неправильн", StringComparison.OrdinalIgnoreCase)
                || text.Contains("не существует", StringComparison.OrdinalIgnoreCase)
                || text.Contains("заблокирован", StringComparison.OrdinalIgnoreCase);

            return rejected ? "Рутрекер не принял логин или пароль." : null;
        }
        catch (Exception exception) when (IsPlaywrightFailure(exception))
        {
            return null;
        }
    }

    private static async Task<bool> IsChallengePageAsync(IPage page)
    {
        string? title = await TryGetTitleAsync(page).ConfigureAwait(false);
        if (title is not null && IsChallengeTitle(title))
        {
            return true;
        }

        string? content = await TryGetContentAsync(page).ConfigureAwait(false);
        return content is not null
            && (content.Contains("cf_chl_opt", StringComparison.Ordinal)
                || content.Contains("challenge-platform", StringComparison.Ordinal)
                || content.Contains("__cf_chl", StringComparison.Ordinal));
    }

    private static async Task<RutrackerException> CreateLoginExceptionAsync(
        IPage page,
        string reason)
    {
        if (await IsChallengePageAsync(page).ConfigureAwait(false))
        {
            return new RutrackerException(
                RutrackerFailureKind.Challenge,
                "Cloudflare не пропустил страницу входа в рутрекер. Повторите позже.");
        }

        return new RutrackerException(
            RutrackerFailureKind.LoginFailed,
            $"Не удалось войти в рутрекер: {reason}. Проверьте логин и пароль.");
    }

    /// <summary>
    /// Собирает фрагмент Cookie из кук Cloudflare. Куки рутрекера подмешиваются отдельно
    /// из профиля или из настроек; сюда попадают только <c>cf_*</c> и <c>__cf_bm</c>.
    /// </summary>
    private static string? BuildClearanceCookie(
        IReadOnlyList<BrowserContextCookiesResult> cookies,
        out DateTime? expiresAtUtc)
    {
        expiresAtUtc = null;

        var parts = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        DateTime? clearanceExpires = null;

        foreach (BrowserContextCookiesResult cookie in cookies)
        {
            if (cookie.Name is not ("cf_clearance" or "__cf_bm"))
            {
                continue;
            }

            if (string.IsNullOrEmpty(cookie.Value) || !seen.Add(cookie.Name))
            {
                continue;
            }

            parts.Add($"{cookie.Name}={cookie.Value}");

            if (cookie.Name == "cf_clearance" && cookie.Expires > 0)
            {
                clearanceExpires = DateTimeOffset
                    .FromUnixTimeSeconds((long)cookie.Expires)
                    .UtcDateTime;
            }
        }

        if (!parts.Any(part => part.StartsWith("cf_clearance=", StringComparison.Ordinal)))
        {
            return null;
        }

        expiresAtUtc = clearanceExpires;
        return string.Join("; ", parts);
    }

    /// <summary>
    /// Кладёт куку рутрекера в контекст браузера: без неё <c>dl.php</c> отвечает
    /// «attachment data not found» даже после пройденной проверки Cloudflare.
    /// </summary>
    private static async Task AddRutrackerCookiesAsync(IBrowserContext context, string cookieHeader)
    {
        if (string.IsNullOrWhiteSpace(cookieHeader))
        {
            return;
        }

        var byName = new Dictionary<string, Cookie>(StringComparer.Ordinal);

        foreach (string part in cookieHeader.Split(
                     ';',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int separator = part.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            string name = part[..separator].Trim();
            string value = part[(separator + 1)..].Trim();

            if (name.Length == 0 || value.Length == 0 || IsCookieAttribute(name))
            {
                continue;
            }

            byName[name] = new Cookie
            {
                Name = name,
                Value = value,
                Domain = ".rutracker.org",
                Path = "/",
            };
        }

        if (byName.Count > 0)
        {
            await context.AddCookiesAsync(byName.Values).ConfigureAwait(false);
        }
    }

    private static bool IsCookieAttribute(string name) =>
        name.Equals("path", StringComparison.OrdinalIgnoreCase)
        || name.Equals("domain", StringComparison.OrdinalIgnoreCase)
        || name.Equals("expires", StringComparison.OrdinalIgnoreCase)
        || name.Equals("max-age", StringComparison.OrdinalIgnoreCase)
        || name.Equals("secure", StringComparison.OrdinalIgnoreCase)
        || name.Equals("httponly", StringComparison.OrdinalIgnoreCase)
        || name.Equals("samesite", StringComparison.OrdinalIgnoreCase);

    /// <summary>Читает актуальный пропуск Cloudflare из контекста после скачивания.</summary>
    private async Task<CloudflareClearance?> ReadClearanceAsync(IBrowserContext context, IPage page)
    {
        try
        {
            IReadOnlyList<BrowserContextCookiesResult> cookies = await context
                .CookiesAsync(CookieUrls)
                .ConfigureAwait(false);

            string? cookie = BuildClearanceCookie(cookies, out DateTime? expiresAtUtc);
            if (cookie is null)
            {
                return null;
            }

            string? userAgent = await TryEvaluateAsync(page, "navigator.userAgent")
                .ConfigureAwait(false);

            return userAgent is null
                ? null
                : new CloudflareClearance(cookie, userAgent, UtcNow(), expiresAtUtc);
        }
        catch (Exception exception) when (IsPlaywrightFailure(exception))
        {
            return null;
        }
    }

    /// <summary>Объясняет, почему скачивание не началось: челлендж, кука или пустой топик.</summary>
    private static async Task<RutrackerException> CreateNoDownloadExceptionAsync(
        IPage page,
        int topicId)
    {
        string? title = await TryGetTitleAsync(page).ConfigureAwait(false);
        string content = await TryGetContentAsync(page).ConfigureAwait(false) ?? string.Empty;

        if (IsChallengeTitle(title ?? string.Empty)
            || content.Contains("cf_chl_opt", StringComparison.Ordinal)
            || content.Contains("challenge-platform", StringComparison.Ordinal)
            || content.Contains("__cf_chl", StringComparison.Ordinal))
        {
            return new RutrackerException(
                RutrackerFailureKind.Challenge,
                "Cloudflare не пропустил браузер к dl.php. Повторите позже.");
        }

        if (content.Contains("attachment data not found", StringComparison.OrdinalIgnoreCase)
            || content.Contains("Войдите", StringComparison.Ordinal))
        {
            return new RutrackerException(
                RutrackerFailureKind.CookieInvalid,
                $"Рутрекер не отдал .torrent для топика {topicId}: проверьте куку в настройках.");
        }

        return new RutrackerException(
            RutrackerFailureKind.InvalidPayload,
            $"Рутрекер вернул страницу вместо .torrent для топика {topicId}.");
    }

    /// <summary>Цепочка редиректов последней навигации: от исходного адреса до конечного.</summary>
    private static async Task<string> DescribeRedirectsAsync(IResponse response)
    {
        var chain = new List<string>();

        IResponse? current = response;

        while (current is not null)
        {
            chain.Add($"{current.Status} {current.Url}");

            IRequest? previous = current.Request.RedirectedFrom;
            current = previous is null
                ? null
                : await previous.ResponseAsync().ConfigureAwait(false);
        }

        chain.Reverse();
        return string.Join(" → ", chain);
    }

    private static string? TryGetUrl(IPage page)
    {
        try
        {
            return page.Url;
        }
        catch (Exception exception) when (IsPlaywrightFailure(exception))
        {
            return null;
        }
    }

    private static async Task<string?> TryGetContentAsync(IPage page)
    {
        try
        {
            return await page.ContentAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (IsPlaywrightFailure(exception))
        {
            return null;
        }
    }

    private static async Task<string?> TryGetTitleAsync(IPage page)
    {
        try
        {
            return await page.TitleAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (IsPlaywrightFailure(exception))
        {
            return null;
        }
    }

    private static async Task<string?> TryEvaluateAsync(IPage page, string script)
    {
        try
        {
            return await page.EvaluateAsync<string>(script).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsPlaywrightFailure(exception))
        {
            return null;
        }
    }

    private static async Task CloseQuietlyAsync(IPage page)
    {
        try
        {
            await page.CloseAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (IsPlaywrightFailure(exception))
        {
            // Страница уже закрыта или контекст перезапускается — это не ошибка операции.
        }
    }

    /// <summary>
    /// Сбой Playwright при работе со страницей. С Playwright 1.44 таймаут — это отдельный
    /// <see cref="TimeoutException"/>, а не <see cref="PlaywrightException"/>: если ловить
    /// только последний, ожидание исчезнувшего элемента вылетает наружу как 500.
    /// </summary>
    private static bool IsPlaywrightFailure(Exception exception) =>
        exception is PlaywrightException or TimeoutException;

    private static bool IsDownloadStarting(PlaywrightException exception) =>
        exception.Message.Contains("Download is starting", StringComparison.OrdinalIgnoreCase);

    private static bool IsChallengeTitle(string title) =>
        title.Contains("Один момент", StringComparison.OrdinalIgnoreCase)
        || title.Contains("Just a moment", StringComparison.OrdinalIgnoreCase)
        || title.Contains("Attention Required", StringComparison.OrdinalIgnoreCase);

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}
