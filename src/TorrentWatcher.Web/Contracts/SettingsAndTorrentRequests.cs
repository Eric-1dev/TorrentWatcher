using System.ComponentModel.DataAnnotations;

namespace TorrentWatcher.Web.Contracts;

/// <summary>Тело запроса на добавление торрента по ссылке на топик.</summary>
public sealed record AddTorrentRequest(
    [Required(AllowEmptyStrings = false, ErrorMessage = "Ссылка на топик обязательна.")]
    string Url);

/// <summary>Тело запроса на паузу/возврат торрента в автоскан.</summary>
public sealed record SetEnabledRequest(bool IsEnabled);

/// <summary>
/// Тело PUT /api/settings. <see cref="RutrackerCookie"/> = <c>null</c> — не менять куку:
/// UI присылает только новое значение, а не маску.
/// </summary>
public sealed record UpdateSettingsRequest(
    string? RutrackerCookie,
    int ScanIntervalMinutes,
    bool AutoScanEnabled,
    int RequestDelayMs,
    int RequestTimeoutSeconds);
