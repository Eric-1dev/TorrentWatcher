using TorrentWatcher.Application.Abstractions;
using TorrentWatcher.Application.Exceptions;
using TorrentWatcher.Application.Rutracker;
using TorrentWatcher.Application.Services;
using TorrentWatcher.Application.Settings;
using TorrentWatcher.Domain.Entities;
using TorrentWatcher.UnitTests.Support;

namespace TorrentWatcher.UnitTests.Application.Rutracker;

public sealed class CloudflareAwareRutrackerClientTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task NoClearance_SendsRutrackerCookieOnly()
    {
        var transport = new FakeTransport(_ => [1, 2, 3]);
        var client = CreateClient(transport, new FakeClearanceProvider(null));

        await client.DownloadTorrentAsync(Request(), CancellationToken.None);

        RutrackerDownloadRequest sent = Assert.Single(transport.Requests);
        Assert.Equal("bb_session=secret", sent.Cookie);
        Assert.Null(sent.UserAgent);
    }

    [Fact]
    public async Task ExistingClearance_IsAppendedWithItsUserAgent()
    {
        var clearance = new CloudflareClearance(
            "cf_clearance=ready",
            "UA-ready",
            Now.UtcDateTime,
            null);

        var transport = new FakeTransport(_ => [1, 2, 3]);
        var client = CreateClient(transport, new FakeClearanceProvider(clearance));

        await client.DownloadTorrentAsync(Request(), CancellationToken.None);

        RutrackerDownloadRequest sent = Assert.Single(transport.Requests);
        Assert.Equal("bb_session=secret; cf_clearance=ready", sent.Cookie);
        Assert.Equal("UA-ready", sent.UserAgent);
    }

    [Fact]
    public async Task Challenge_IsSolvedOnceAndRequestRepeatsWithFreshClearance()
    {
        var solver = new FakeClearanceProvider(null);
        var transport = new FakeTransport(
            _ => throw Challenge(),
            _ => [9, 8, 7]);

        var client = CreateClient(transport, solver);

        byte[] payload = await client.DownloadTorrentAsync(Request(), CancellationToken.None);

        Assert.Equal([9, 8, 7], payload);
        Assert.Equal(1, solver.SolveCount);
        Assert.Equal(2, transport.Requests.Count);

        RutrackerDownloadRequest retry = transport.Requests[1];
        Assert.Equal("bb_session=secret; cf_clearance=fresh", retry.Cookie);
        Assert.Equal("UA-fresh", retry.UserAgent);
        Assert.Null(solver.RejectedMessage);
    }

    [Fact]
    public async Task ChallengeAfterSolve_ReportsRejectionAndRethrows()
    {
        var solver = new FakeClearanceProvider(null);
        var transport = new FakeTransport(_ => throw Challenge(), _ => throw Challenge());

        var client = CreateClient(transport, solver);

        RutrackerException exception = await Assert.ThrowsAsync<RutrackerException>(
            () => client.DownloadTorrentAsync(Request(), CancellationToken.None));

        Assert.Equal(RutrackerFailureKind.Challenge, exception.Kind);
        Assert.Equal(1, solver.SolveCount);
        Assert.Null(solver.Current);
        Assert.NotNull(solver.RejectedMessage);
        Assert.Equal(Now.UtcDateTime, solver.RejectedAtUtc);
    }

    [Fact]
    public async Task ChallengeAfterSolve_FallsBackToBrowserAndStaysOnIt()
    {
        var solver = new FakeClearanceProvider(null);
        var transport = new FakeTransport(_ => throw Challenge(), _ => throw Challenge());
        var browser = new FakeBrowser(_ => BrowserPayload([4, 5, 6]));
        var client = CreateClient(transport, solver, browser: browser);

        byte[] payload = await client.DownloadTorrentAsync(Request(), CancellationToken.None);

        Assert.Equal([4, 5, 6], payload);
        Assert.Equal(1, browser.CallCount);
        Assert.Equal("bb_session=secret", browser.LastRutrackerCookie);
        Assert.NotNull(solver.Saved);
        Assert.Equal(2, transport.Requests.Count);

        // После первого успешного скачивания HTTP больше не пробуем.
        await client.DownloadTorrentAsync(Request(), CancellationToken.None);

        Assert.Equal(2, browser.CallCount);
        Assert.Equal(2, transport.Requests.Count);
    }

    [Fact]
    public async Task BrowserOnly_IsSharedBetweenClientInstances()
    {
        var state = new CloudflareTransportState();
        var transport = new FakeTransport(_ => throw Challenge(), _ => throw Challenge());
        var browser = new FakeBrowser(_ => BrowserPayload([7]));

        var first = CreateClient(
            transport,
            new FakeClearanceProvider(null),
            browser: browser,
            transportState: state);
        await first.DownloadTorrentAsync(Request(), CancellationToken.None);

        int httpCalls = transport.Requests.Count;

        var second = CreateClient(
            transport,
            new FakeClearanceProvider(null),
            browser: browser,
            transportState: state);
        byte[] payload = await second.DownloadTorrentAsync(Request(), CancellationToken.None);

        Assert.Equal([7], payload);
        Assert.Equal(httpCalls, transport.Requests.Count);
        Assert.Equal(2, browser.CallCount);
    }

    [Fact]
    public async Task ClearanceEmbeddedInRutrackerCookie_IsNotDuplicated()
    {
        var clearance = new CloudflareClearance(
            "cf_clearance=ready",
            "UA-ready",
            Now.UtcDateTime,
            null);

        var transport = new FakeTransport(_ => [1]);
        var client = CreateClient(transport, new FakeClearanceProvider(clearance));

        await client.DownloadTorrentAsync(
            new RutrackerDownloadRequest(42, "bb_session=secret; cf_clearance=manual", Timeout),
            CancellationToken.None);

        Assert.Equal(
            "bb_session=secret; cf_clearance=manual",
            Assert.Single(transport.Requests).Cookie);
    }

    private static CloudflareAwareRutrackerClient CreateClient(
        IRutrackerHttpClient transport,
        FakeClearanceProvider clearance,
        string rutrackerCookie = "bb_session=secret",
        FakeBrowser? browser = null,
        CloudflareTransportState? transportState = null)
    {
        var settings = new SettingsProvider();
        settings.Apply(AppSettingsSnapshot.FromEntity(new AppSetting
        {
            RutrackerCookie = rutrackerCookie,
        }));

        return new CloudflareAwareRutrackerClient(
            transport,
            browser ?? new FakeBrowser(_ => throw Challenge()),
            settings,
            clearance,
            transportState ?? new CloudflareTransportState(),
            new FixedTimeProvider(Now));
    }

    private static BrowserTorrentDownloadResult BrowserPayload(byte[] payload) =>
        new(
            payload,
            new CloudflareClearance("cf_clearance=browser", "UA-browser", Now.UtcDateTime, null));

    private static RutrackerDownloadRequest Request() =>
        new(42, "bb_session=secret", Timeout);

    private static RutrackerException Challenge() =>
        new(RutrackerFailureKind.Challenge, "Cloudflare требует проверки браузера.");

    private sealed class FakeTransport : IRutrackerHttpClient
    {
        private readonly Queue<Func<RutrackerDownloadRequest, byte[]>> _responses;

        public FakeTransport(params Func<RutrackerDownloadRequest, byte[]>[] responses)
        {
            _responses = new Queue<Func<RutrackerDownloadRequest, byte[]>>(responses);
        }

        public List<RutrackerDownloadRequest> Requests { get; } = [];

        public Task<byte[]> DownloadTorrentAsync(
            RutrackerDownloadRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_responses.Dequeue()(request));
        }
    }

    private sealed class FakeBrowser : IBrowserTorrentDownloader
    {
        private readonly Func<int, BrowserTorrentDownloadResult> _handler;

        public FakeBrowser(Func<int, BrowserTorrentDownloadResult> handler)
        {
            _handler = handler;
        }

        public int CallCount { get; private set; }

        public string? LastRutrackerCookie { get; private set; }

        public Task<BrowserTorrentDownloadResult> DownloadTorrentAsync(
            int topicId,
            string rutrackerCookie,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastRutrackerCookie = rutrackerCookie;
            return Task.FromResult(_handler(topicId));
        }
    }

    private sealed class FakeClearanceProvider : ICloudflareClearanceProvider
    {
        public FakeClearanceProvider(CloudflareClearance? current)
        {
            Current = current;
        }

        public CloudflareClearance? Current { get; private set; }

        public CloudflareClearance? Saved { get; private set; }

        public CloudflareClearance Solved { get; } = new(
            "cf_clearance=fresh",
            "UA-fresh",
            Now.UtcDateTime,
            null);

        public int SolveCount { get; private set; }

        public string? RejectedMessage { get; private set; }

        public DateTime? RejectedAtUtc { get; private set; }

        public Task<CloudflareClearance> SolveAsync(int topicId, CancellationToken cancellationToken)
        {
            SolveCount++;
            Current = Solved;
            return Task.FromResult(Solved);
        }

        public Task SaveAsync(CloudflareClearance clearance, CancellationToken cancellationToken)
        {
            Saved = clearance;
            Current = clearance;
            return Task.CompletedTask;
        }

        public Task ReportRejectedAsync(
            string message,
            DateTime failedAtUtc,
            CancellationToken cancellationToken)
        {
            RejectedMessage = message;
            RejectedAtUtc = failedAtUtc;
            Current = null;
            return Task.CompletedTask;
        }
    }
}
