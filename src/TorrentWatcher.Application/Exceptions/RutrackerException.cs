namespace TorrentWatcher.Application.Exceptions;

/// <summary>Классификация сбоя при обращении к рутрекеру.</summary>
public enum RutrackerFailureKind
{
    /// <summary>Рутрекер отклонил куку (403/401).</summary>
    CookieInvalid,

    /// <summary>Топик не найден (404/410).</summary>
    NotFound,

    /// <summary>Сеть, таймаут или неожиданный HTTP-статус.</summary>
    Network,

    /// <summary>Ответ получен, но это не корректный .torrent (HTML, мусор, слишком большой файл).</summary>
    InvalidPayload,
}

/// <summary>
/// Сбой обращения к рутрекеру с машинным кодом для API. Наследует <see cref="AppException"/>,
/// чтобы middleware одинаково превращал ошибку в ProblemDetails.
/// </summary>
public sealed class RutrackerException : AppException
{
    public RutrackerException(RutrackerFailureKind kind, string message)
        : base(MapCode(kind), message)
    {
        Kind = kind;
    }

    public RutrackerException(RutrackerFailureKind kind, string message, Exception? innerException)
        : base(MapCode(kind), message, innerException)
    {
        Kind = kind;
    }

    public RutrackerFailureKind Kind { get; }

    private static string MapCode(RutrackerFailureKind kind) => kind switch
    {
        RutrackerFailureKind.CookieInvalid => ErrorCodes.RutrackerCookieInvalid,
        RutrackerFailureKind.NotFound => ErrorCodes.TopicNotFound,
        RutrackerFailureKind.Network => ErrorCodes.RutrackerUnavailable,
        RutrackerFailureKind.InvalidPayload => ErrorCodes.RutrackerInvalidPayload,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Неизвестный вид сбоя рутрекера."),
    };
}
