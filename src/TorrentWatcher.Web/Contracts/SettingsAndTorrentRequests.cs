using System.ComponentModel.DataAnnotations;

namespace TorrentWatcher.Web.Contracts;

/// <summary>Тело запроса на добавление торрента по ссылке на топик.</summary>
public sealed record AddTorrentRequest(
    [Required(AllowEmptyStrings = false, ErrorMessage = "Ссылка на топик обязательна.")]
    string Url);

/// <summary>Тело запроса на паузу/возврат торрента в автоскан.</summary>
public sealed record SetEnabledRequest(bool IsEnabled);

/// <summary>
/// Тело PUT /api/settings. <c>null</c> у секретов — не менять: UI присылает только новое
/// значение, а не маску. Пустая строка очищает логин, пароль или ручную куку.
/// </summary>
public sealed record UpdateSettingsRequest(
    string? RutrackerCookie,
    int ScanIntervalMinutes,
    bool AutoScanEnabled,
    int RequestDelayMs,
    int RequestTimeoutSeconds,
    string? CloudflareClearanceCookie = null,
    string? CloudflareUserAgent = null,
    string? RutrackerLogin = null,
    string? RutrackerPassword = null,
    string? QbittorrentUrl = null,
    string? QbittorrentUsername = null,
    string? QbittorrentPassword = null,
    bool? QbittorrentAutoApply = null);
