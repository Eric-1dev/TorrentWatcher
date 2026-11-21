namespace TorrentWatcher.Application.Dtos;

/// <summary>
/// Данные формы настроек. Значение <c>null</c> у секретов означает «не менять»:
/// UI присылает их только когда пользователь ввёл новое значение. Пустая строка
/// у <see cref="RutrackerLogin"/>, <see cref="RutrackerPassword"/> и
/// <see cref="CloudflareClearanceCookie"/> означает «очистить».
/// </summary>
/// <param name="RutrackerCookie">
/// Аварийный ручной сеанс рутрекера. Обычно не используется: куку заполняет приложение
/// после входа по логину и паролю.
/// </param>
/// <param name="RutrackerLogin">Логин рутрекера; <c>null</c> — не менять.</param>
/// <param name="RutrackerPassword">Пароль в открытом виде; <c>null</c> — не менять.</param>
/// <param name="CloudflareClearanceCookie">
/// Ручной пропуск Cloudflare (<c>cf_clearance=…</c>). <c>null</c> — не менять,
/// пустая строка — сбросить.
/// </param>
/// <param name="CloudflareUserAgent">
/// UA, под которым выдан пропуск. Смена UA без пропуска сбрасывает старый clearance.
/// </param>
/// <param name="QbittorrentUrl">Адрес Web UI qBittorrent; <c>null</c> — не менять, пустая строка — очистить.</param>
/// <param name="QbittorrentUsername">Логин qBittorrent; <c>null</c> — не менять.</param>
/// <param name="QbittorrentPassword">Пароль qBittorrent в открытом виде; <c>null</c> — не менять.</param>
/// <param name="QbittorrentAutoApply">Авто-применение обновлений; <c>null</c> — не менять.</param>
public sealed record SettingsUpdate(
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
