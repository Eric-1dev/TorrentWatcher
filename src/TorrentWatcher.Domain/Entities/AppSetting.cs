namespace TorrentWatcher.Domain.Entities;

/// <summary>
/// Единственная строка настроек приложения. Кука хранится в plaintext: приложение
/// self-hosted и не имеет авторизации, поэтому файл БД и так доступен владельцу.
/// </summary>
public sealed class AppSetting
{
    public const int DefaultId = 1;
    public const int MinScanIntervalMinutes = 1;
    public const int MaxScanIntervalMinutes = 1440;
    public const int DefaultScanIntervalMinutes = 60;
    public const int DefaultRequestDelayMs = 1500;
    public const int DefaultRequestTimeoutSeconds = 30;

    public int Id { get; set; } = DefaultId;

    /// <summary>Кука рутрекера вида <c>bb_session=…; bb_data=…</c>.</summary>
    public string RutrackerCookie { get; set; } = string.Empty;

    public DateTime? CookieVerifiedAtUtc { get; set; }

    public string? CookieLastError { get; set; }

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
