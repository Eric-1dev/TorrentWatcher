using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using TorrentWatcher.Application.Exceptions;
using TorrentWatcher.Web.Middleware;

namespace TorrentWatcher.IntegrationTests.Web;

public sealed class ExceptionHandlingMiddlewareTests
{
    [Theory]
    [InlineData(ErrorCodes.InvalidUrl, StatusCodes.Status400BadRequest)]
    [InlineData(ErrorCodes.SettingsInvalid, StatusCodes.Status400BadRequest)]
    [InlineData(ErrorCodes.RutrackerCookieInvalid, StatusCodes.Status401Unauthorized)]
    [InlineData(ErrorCodes.TorrentNotFound, StatusCodes.Status404NotFound)]
    [InlineData(ErrorCodes.TopicNotFound, StatusCodes.Status404NotFound)]
    [InlineData(ErrorCodes.DuplicateTopic, StatusCodes.Status409Conflict)]
    [InlineData(ErrorCodes.ScanAlreadyRunning, StatusCodes.Status409Conflict)]
    [InlineData(ErrorCodes.RutrackerUnavailable, StatusCodes.Status502BadGateway)]
    [InlineData(ErrorCodes.RutrackerInvalidPayload, StatusCodes.Status502BadGateway)]
    [InlineData(ErrorCodes.StorageFailure, StatusCodes.Status500InternalServerError)]
    public async Task AppException_BecomesProblemDetailsWithCode(string code, int expectedStatus)
    {
        (HttpContext context, JsonDocument problem) = await InvokeAsync(
            _ => throw new AppException(code, "Понятное описание."));

        Assert.Equal(expectedStatus, context.Response.StatusCode);
        Assert.StartsWith("application/problem+json", context.Response.ContentType);

        Assert.Equal(code, problem.RootElement.GetProperty("code").GetString());
        Assert.Equal("Понятное описание.", problem.RootElement.GetProperty("detail").GetString());
        Assert.Equal(expectedStatus, problem.RootElement.GetProperty("status").GetInt32());
        Assert.False(problem.RootElement.TryGetProperty("stackTrace", out _));
        Assert.False(problem.RootElement.TryGetProperty("exception", out _));
        Assert.True(problem.RootElement.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task UnexpectedException_ReturnsGenericProblemWithoutDetails()
    {
        (HttpContext context, JsonDocument problem) = await InvokeAsync(
            _ => throw new InvalidOperationException("Секретные внутренности."));

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal(ErrorCodes.InternalError, problem.RootElement.GetProperty("code").GetString());

        string detail = problem.RootElement.GetProperty("detail").GetString()!;
        Assert.DoesNotContain("Секретные внутренности", detail);
    }

    [Fact]
    public async Task ClientCancellation_DoesNotWriteProblemDetails()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var context = new DefaultHttpContext();
        context.RequestAborted = cts.Token;
        context.Response.Body = new MemoryStream();

        var middleware = new ExceptionHandlingMiddleware(
            _ => throw new OperationCanceledException(cts.Token),
            NullLogger<ExceptionHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(0, context.Response.Body.Length);
    }

    private static async Task<(HttpContext Context, JsonDocument Problem)> InvokeAsync(
        RequestDelegate next)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/torrents";
        context.Response.Body = new MemoryStream();

        var middleware = new ExceptionHandlingMiddleware(
            next,
            NullLogger<ExceptionHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        JsonDocument problem = await JsonDocument.ParseAsync(context.Response.Body);

        return (context, problem);
    }
}
