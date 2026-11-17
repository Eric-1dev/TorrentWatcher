namespace TorrentWatcher.Application.Exceptions;

/// <summary>
/// Ожидаемая ошибка прикладного слоя с машинным кодом. Middleware превращает её в ProblemDetails,
/// поэтому стектрейс и внутренние детали в ответ не попадают.
/// </summary>
public class AppException : Exception
{
    public AppException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public AppException(string code, string message, Exception? innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    /// <summary>Машинный код из <see cref="ErrorCodes"/>.</summary>
    public string Code { get; }
}
