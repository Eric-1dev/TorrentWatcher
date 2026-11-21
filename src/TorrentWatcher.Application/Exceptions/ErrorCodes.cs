namespace TorrentWatcher.Application.Exceptions;

/// <summary>
/// Машинные коды ошибок API. Используются в <see cref="AppException"/> и попадают в ProblemDetails.
/// </summary>
public static class ErrorCodes
{
    public const string InvalidUrl = "INVALID_URL";
    public const string DuplicateTopic = "DUPLICATE_TOPIC";
    public const string TorrentNotFound = "TORRENT_NOT_FOUND";
    public const string TopicNotFound = "TOPIC_NOT_FOUND";
    public const string RutrackerCookieInvalid = "RUTRACKER_COOKIE_INVALID";
    public const string RutrackerLoginFailed = "RUTRACKER_LOGIN_FAILED";
    public const string RutrackerLoginCaptchaRequired = "RUTRACKER_LOGIN_CAPTCHA_REQUIRED";
    public const string RutrackerCredentialsUnreadable = "RUTRACKER_CREDENTIALS_UNREADABLE";
    public const string RutrackerChallengeRequired = "RUTRACKER_CHALLENGE_REQUIRED";
    public const string RutrackerUnavailable = "RUTRACKER_UNAVAILABLE";
    public const string RutrackerInvalidPayload = "RUTRACKER_INVALID_PAYLOAD";
    public const string StorageFailure = "TORRENT_STORAGE_FAILED";
    public const string QbittorrentNotConfigured = "QBITTORRENT_NOT_CONFIGURED";
    public const string QbittorrentAuthFailed = "QBITTORRENT_AUTH_FAILED";
    public const string QbittorrentUnavailable = "QBITTORRENT_UNAVAILABLE";
    public const string SettingsInvalid = "INVALID_SETTINGS";
    public const string ScanAlreadyRunning = "SCAN_ALREADY_RUNNING";
    public const string InternalError = "INTERNAL_ERROR";
    public const string EndpointNotFound = "ENDPOINT_NOT_FOUND";
}
