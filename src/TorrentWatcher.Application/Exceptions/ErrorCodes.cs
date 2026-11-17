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
    public const string RutrackerUnavailable = "RUTRACKER_UNAVAILABLE";
    public const string RutrackerInvalidPayload = "RUTRACKER_INVALID_PAYLOAD";
    public const string StorageFailure = "TORRENT_STORAGE_FAILED";
    public const string SettingsInvalid = "INVALID_SETTINGS";
    public const string ScanAlreadyRunning = "SCAN_ALREADY_RUNNING";
    public const string InternalError = "INTERNAL_ERROR";
    public const string EndpointNotFound = "ENDPOINT_NOT_FOUND";
}
