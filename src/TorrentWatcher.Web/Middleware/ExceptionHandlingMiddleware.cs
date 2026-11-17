using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using TorrentWatcher.Application.Exceptions;

namespace TorrentWatcher.Web.Middleware;

/// <summary>
/// Превращает ожидаемые <see cref="AppException"/> и непредвиденные исключения в RFC 7807
/// без стектрейсов. Машинный код кладётся в расширение <c>code</c>.
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Клиент ушёл — писать ProblemDetails некому.
        }
        catch (AppException exception)
        {
            await WriteProblemAsync(context, exception.Code, exception.Message);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Необработанная ошибка при {Method} {Path}",
                context.Request.Method,
                context.Request.Path);

            await WriteProblemAsync(
                context,
                ErrorCodes.InternalError,
                "Внутренняя ошибка сервера. Подробности — в журнале приложения.");
        }
    }

    private async Task WriteProblemAsync(HttpContext context, string code, string detail)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        int status = MapStatusCode(code);

        var problem = new ProblemDetails
        {
            Status = status,
            Title = TitleFor(status),
            Detail = detail,
            Instance = context.Request.Path,
            Type = $"https://torrentwatcher.local/problems/{code.ToLowerInvariant()}",
        };

        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = context.TraceIdentifier;

        context.Response.Clear();
        context.Response.StatusCode = status;

        await context.Response.WriteAsJsonAsync(
            problem,
            options: null,
            contentType: "application/problem+json",
            context.RequestAborted);
    }

    private static int MapStatusCode(string code) => code switch
    {
        ErrorCodes.InvalidUrl => StatusCodes.Status400BadRequest,
        ErrorCodes.SettingsInvalid => StatusCodes.Status400BadRequest,
        ErrorCodes.RutrackerCookieInvalid => StatusCodes.Status401Unauthorized,
        ErrorCodes.TorrentNotFound => StatusCodes.Status404NotFound,
        ErrorCodes.TopicNotFound => StatusCodes.Status404NotFound,
        ErrorCodes.DuplicateTopic => StatusCodes.Status409Conflict,
        ErrorCodes.ScanAlreadyRunning => StatusCodes.Status409Conflict,
        ErrorCodes.RutrackerUnavailable => StatusCodes.Status502BadGateway,
        ErrorCodes.RutrackerInvalidPayload => StatusCodes.Status502BadGateway,
        ErrorCodes.StorageFailure => StatusCodes.Status500InternalServerError,
        _ => StatusCodes.Status500InternalServerError,
    };

    private static string TitleFor(int status) => status switch
    {
        StatusCodes.Status400BadRequest => "Некорректный запрос",
        StatusCodes.Status401Unauthorized => "Кука рутрекера не принята",
        StatusCodes.Status404NotFound => "Не найдено",
        StatusCodes.Status409Conflict => "Конфликт",
        StatusCodes.Status502BadGateway => "Ошибка рутрекера",
        _ => "Внутренняя ошибка сервера",
    };
}
