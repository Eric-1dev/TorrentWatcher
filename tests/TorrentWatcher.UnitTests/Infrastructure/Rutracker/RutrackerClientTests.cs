using System.Net;
using System.Text;
using TorrentWatcher.Application.Exceptions;
using TorrentWatcher.Application.Rutracker;
using TorrentWatcher.Infrastructure.Rutracker;
using TorrentWatcher.UnitTests.Domain.Parsing;
using TorrentWatcher.UnitTests.Support;

namespace TorrentWatcher.UnitTests.Infrastructure.Rutracker;

public sealed class RutrackerClientTests
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task DownloadTorrentAsync_ReturnsPayloadAndSendsExpectedRequest()
    {
        (byte[] torrent, _) = TorrentFixtures.SingleFile();
        HttpRequestMessage? captured = null;

        var handler = new StubHttpMessageHandler((request, _) =>
        {
            captured = request;
            return Task.FromResult(Ok(torrent));
        });

        byte[] payload = await CreateClient(handler).DownloadTorrentAsync(
            new RutrackerDownloadRequest(123456, "bb_session=secret; bb_data=more", RequestTimeout),
            CancellationToken.None);

        Assert.Equal(torrent, payload);

        HttpRequestMessage sent = Assert.IsType<HttpRequestMessage>(captured);
        Assert.Equal(HttpMethod.Get, sent.Method);
        Assert.Equal("https://rutracker.org/forum/dl.php?t=123456", sent.RequestUri?.ToString());
        Assert.Equal("https://rutracker.org/forum/viewtopic.php?t=123456", sent.Headers.Referrer?.ToString());
        Assert.Equal("bb_session=secret; bb_data=more", Assert.Single(sent.Headers.GetValues("Cookie")));
        Assert.NotEmpty(sent.Headers.UserAgent);
    }

    [Fact]
    public async Task HtmlResponse_ThrowsInvalidPayload()
    {
        var handler = ContentHandler(
            "<html><body>Вы не авторизованы</body></html>",
            "text/html");

        RutrackerException exception = await Assert.ThrowsAsync<RutrackerException>(
            () => CreateClient(handler).DownloadTorrentAsync(Request(), CancellationToken.None));

        Assert.Equal(RutrackerFailureKind.InvalidPayload, exception.Kind);
        Assert.Equal(ErrorCodes.RutrackerInvalidPayload, exception.Code);
    }

    [Fact]
    public async Task BencodeWithoutAnnounce_ThrowsInvalidPayload()
    {
        var handler = ContentHandler("d3:foo3:bare", "application/x-bittorrent");

        RutrackerException exception = await Assert.ThrowsAsync<RutrackerException>(
            () => CreateClient(handler).DownloadTorrentAsync(Request(), CancellationToken.None));

        Assert.Equal(RutrackerFailureKind.InvalidPayload, exception.Kind);
    }

    [Fact]
    public async Task EmptyResponse_ThrowsInvalidPayload()
    {
        var handler = ContentHandler(string.Empty, "application/x-bittorrent");

        RutrackerException exception = await Assert.ThrowsAsync<RutrackerException>(
            () => CreateClient(handler).DownloadTorrentAsync(Request(), CancellationToken.None));

        Assert.Equal(RutrackerFailureKind.InvalidPayload, exception.Kind);
    }

    [Fact]
    public async Task Forbidden_ThrowsCookieInvalid()
    {
        var handler = StatusHandler(HttpStatusCode.Forbidden);

        RutrackerException exception = await Assert.ThrowsAsync<RutrackerException>(
            () => CreateClient(handler).DownloadTorrentAsync(Request(), CancellationToken.None));

        Assert.Equal(RutrackerFailureKind.CookieInvalid, exception.Kind);
        Assert.Equal(ErrorCodes.RutrackerCookieInvalid, exception.Code);
    }

    [Fact]
    public async Task NotFound_ThrowsTopicNotFound()
    {
        var handler = StatusHandler(HttpStatusCode.NotFound);

        RutrackerException exception = await Assert.ThrowsAsync<RutrackerException>(
            () => CreateClient(handler).DownloadTorrentAsync(Request(), CancellationToken.None));

        Assert.Equal(RutrackerFailureKind.NotFound, exception.Kind);
        Assert.Equal(ErrorCodes.TopicNotFound, exception.Code);
    }

    [Fact]
    public async Task ServerError_ThrowsUnavailable()
    {
        var handler = StatusHandler(HttpStatusCode.InternalServerError);

        RutrackerException exception = await Assert.ThrowsAsync<RutrackerException>(
            () => CreateClient(handler).DownloadTorrentAsync(Request(), CancellationToken.None));

        Assert.Equal(RutrackerFailureKind.Network, exception.Kind);
        Assert.Equal(ErrorCodes.RutrackerUnavailable, exception.Code);
    }

    [Fact]
    public async Task DeclaredSizeOverLimit_ThrowsInvalidPayloadWithoutReadingBody()
    {
        var content = new ByteArrayContent([1, 2, 3]);
        content.Headers.ContentLength = RutrackerClient.MaxTorrentBytes + 1L;

        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = content,
        }));

        RutrackerException exception = await Assert.ThrowsAsync<RutrackerException>(
            () => CreateClient(handler).DownloadTorrentAsync(Request(), CancellationToken.None));

        Assert.Equal(RutrackerFailureKind.InvalidPayload, exception.Kind);
    }

    [Fact]
    public async Task Timeout_ThrowsUnavailable()
    {
        var handler = new StubHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        RutrackerException exception = await Assert.ThrowsAsync<RutrackerException>(
            () => CreateClient(handler).DownloadTorrentAsync(
                new RutrackerDownloadRequest(42, "bb_session=x", TimeSpan.FromMilliseconds(50)),
                CancellationToken.None));

        Assert.Equal(RutrackerFailureKind.Network, exception.Kind);
        Assert.Equal(ErrorCodes.RutrackerUnavailable, exception.Code);
    }

    [Fact]
    public async Task CallerCancellation_PropagatesOperationCanceled()
    {
        var handler = new StubHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateClient(handler).DownloadTorrentAsync(Request(), cancellation.Token));
    }

    private static RutrackerClient CreateClient(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan });

    private static RutrackerDownloadRequest Request() =>
        new(42, "bb_session=secret", RequestTimeout);

    private static HttpResponseMessage Ok(byte[] torrent) =>
        new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(torrent),
        };

    private static StubHttpMessageHandler ContentHandler(string body, string mediaType) =>
        new((_, _) =>
        {
            var content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });

    private static StubHttpMessageHandler StatusHandler(HttpStatusCode statusCode) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(statusCode)));
}
