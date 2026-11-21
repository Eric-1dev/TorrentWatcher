namespace TorrentWatcher.Domain.Entities;

/// <summary>
/// Единственная строка настроек приложения. Кука рутрекера хранится в plaintext как кэш
/// сеанса: приложение self-hosted и не имеет авторизации, поэтому файл БД и так доступен
/// владельцу. Пароль рутрекера шифруется <c>ISecretProtector</c> и в открытом виде не хранится.
/// </summary>
public sealed class AppSetting
{
    public const int DefaultId = 1;
    public const int MinScanIntervalMinutes = 1;
    public const int MaxScanIntervalMinutes = 1440;
    public const int DefaultScanIntervalMinutes = 60;
    public const int DefaultRequestDelayMs = 1500;
    public const int DefaultRequestTimeoutSeconds = 30;
    public const int MaxLoginLength = 100;
    public const int MaxPasswordLength = 200;
    public const int MaxUrlLength = 512;

    public int Id { get; set; } = DefaultId;

    /// <summary>
    /// Кука сеанса рутрекера вида <c>bb_session=…; bb_data=…</c>. Заполняется приложением
    /// после входа по логину/паролю; вручную задаётся только как аварийный вариант.
    /// </summary>
    public string RutrackerCookie { get; set; } = string.Empty;

    /// <summary>Логин рутрекера. Не секрет: отдаётся в UI, чтобы было видно, под кем вход.</summary>
    public string RutrackerLogin { get; set; } = string.Empty;

    /// <summary>
    /// Пароль рутрекера, зашифрованный <c>ISecretProtector</c>. В журнал и API не попадает.
    /// </summary>
    public string RutrackerPasswordProtected { get; set; } = string.Empty;

    public DateTime? CookieVerifiedAtUtc { get; set; }

    public string? CookieLastError { get; set; }

    /// <summary>
    /// Кука Cloudflare (<c>cf_clearance=…</c>), без которой <c>dl.php</c> отдаёт челлендж.
    /// Добывается браузером (Playwright) или вставляется вручную из DevTools.
    /// </summary>
    public string? CloudflareClearanceCookie { get; set; }

    /// <summary>
    /// User-Agent, с которым получен <see cref="CloudflareClearanceCookie"/>: Cloudflare
    /// привязывает clearance к UA, поэтому запросы должны отправлять ровно этот заголовок.
    /// </summary>
    public string? CloudflareUserAgent { get; set; }

    public DateTime? CloudflareClearanceObtainedAtUtc { get; set; }

    /// <summary>Срок жизни clearance, если он известен (у <c>cf_clearance</c> — до года).</summary>
    public DateTime? CloudflareClearanceExpiresAtUtc { get; set; }

    public string? CloudflareLastError { get; set; }

    /// <summary>
    /// Адрес Web UI qBittorrent, например <c>http://plex.local:8080</c>. Пустая строка —
    /// интеграция не настроена, приложение только фиксирует обновления.
    /// </summary>
    public string QbittorrentUrl { get; set; } = string.Empty;

    public string QbittorrentUsername { get; set; } = string.Empty;

    /// <summary>Пароль qBittorrent, зашифрованный <c>ISecretProtector</c>.</summary>
    public string QbittorrentPasswordProtected { get; set; } = string.Empty;

    /// <summary>
    /// Автоматически применять обновления: удалять старую раздачу (файлы оставляем)
    /// и добавлять новую в ту же папку. Ручная кнопка «Применить» работает и без этого.
    /// </summary>
    public bool QbittorrentAutoApply { get; set; }

    public DateTime? QbittorrentVerifiedAtUtc { get; set; }

    public string? QbittorrentLastError { get; set; }

    /// <summary>Глобальный интервал автоскана в минутах, 1…1440.</summary>
    public int ScanIntervalMinutes { get; set; } = DefaultScanIntervalMinutes;

    public bool AutoScanEnabled { get; set; } = true;

    /// <summary>Пауза между запросами к рутрекеру внутри одного скана.</summary>
    public int RequestDelayMs { get; set; } = DefaultRequestDelayMs;

    public int RequestTimeoutSeconds { get; set; } = DefaultRequestTimeoutSeconds;

    /// <summary>Время последнего завершённого скана — для расчёта следующего после рестарта.</summary>
    public DateTime? LastScanAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
