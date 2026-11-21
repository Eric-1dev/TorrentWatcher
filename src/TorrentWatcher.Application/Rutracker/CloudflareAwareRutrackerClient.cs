using TorrentWatcher.Application.Abstractions;
using TorrentWatcher.Application.Exceptions;
using TorrentWatcher.Application.Settings;

namespace TorrentWatcher.Application.Rutracker;

/// <summary>
/// Обёртка над транспортом рутрекера: подставляет действующий пропуск Cloudflare, а при
/// челлендже один раз просит браузер получить новый и повторяет запрос. Скачивание .torrent
/// всегда идёт обычным HTTP, браузер нужен только на время проверки.
/// </summary>
public sealed class CloudflareAwareRutrackerClient : IRutrackerClient
{
    private readonly IRutrackerHttpClient _httpClient;
    private readonly IBrowserTorrentDownloader _browser;
    private readonly ISettingsProvider _settings;
    private readonly ICloudflareClearanceProvider _clearance;
    private readonly CloudflareTransportState _transportState;
    private readonly TimeProvider _timeProvider;

    public CloudflareAwareRutrackerClient(
        IRutrackerHttpClient httpClient,
        IBrowserTorrentDownloader browser,
        ISettingsProvider settings,
        ICloudflareClearanceProvider clearance,
        CloudflareTransportState transportState,
        TimeProvider timeProvider)
    {
        _httpClient = httpClient;
        _browser = browser;
        _settings = settings;
        _clearance = clearance;
        _transportState = transportState;
        _timeProvider = timeProvider;
    }

    public async Task<byte[]> DownloadTorrentAsync(
        RutrackerDownloadRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Cloudflare привязывает пропуск к браузеру: на Linux обычный HttpClient получает
        // челлендж даже со свежим cf_clearance. После первого такого отказа качаем браузером.
        if (_transportState.BrowserOnly)
        {
            try
            {
                return await DownloadWithBrowserAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (RutrackerException browserException)
                when (browserException.Kind == RutrackerFailureKind.Challenge)
            {
                await ReportRejectedAsync(browserException, cancellationToken).ConfigureAwait(false);
                throw;
            }
        }

        try
        {
            return await _httpClient.DownloadTorrentAsync(
                WithClearance(request, _settings.Current, _clearance.Current),
                cancellationToken);
        }
        catch (RutrackerException exception) when (exception.Kind == RutrackerFailureKind.Challenge)
        {
            CloudflareClearance fresh = await _clearance
                .SolveAsync(request.TopicId, cancellationToken)
                .ConfigureAwait(false);

            try
            {
                return await _httpClient.DownloadTorrentAsync(
                    WithClearance(request, _settings.Current, fresh),
                    cancellationToken);
            }
            catch (RutrackerException retryException)
                when (retryException.Kind == RutrackerFailureKind.Challenge)
            {
                // HTTP-клиент не проходит проверку даже со свежим пропуском: дальше
                // скачиваем тем же браузером, который прошёл челлендж.
                try
                {
                    return await DownloadWithBrowserAsync(request, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (RutrackerException browserException)
                    when (browserException.Kind == RutrackerFailureKind.Challenge)
                {
                    await ReportRejectedAsync(browserException, cancellationToken).ConfigureAwait(false);
                    throw;
                }
            }
        }
    }

    private async Task<byte[]> DownloadWithBrowserAsync(
        RutrackerDownloadRequest request,
        CancellationToken cancellationToken)
    {
        BrowserTorrentDownloadResult result = await _browser
            .DownloadTorrentAsync(
                request.TopicId,
                request.Cookie,
                request.Timeout,
                cancellationToken)
            .ConfigureAwait(false);

        _transportState.BrowserOnly = true;

        if (result.Clearance is not null)
        {
            await _clearance.SaveAsync(result.Clearance, cancellationToken).ConfigureAwait(false);
        }

        return result.Payload;
    }

    private Task ReportRejectedAsync(
        RutrackerException exception,
        CancellationToken cancellationToken) =>
        _clearance.ReportRejectedAsync(
            exception.Message,
            _timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);

    private static RutrackerDownloadRequest WithClearance(
        RutrackerDownloadRequest request,
        AppSettingsSnapshot settings,
        CloudflareClearance? clearance)
    {
        string cookie = CombineCookies(request.Cookie, clearance?.Cookie);
        string? userAgent = clearance?.UserAgent ?? settings.CloudflareUserAgent;

        return request with
        {
            Cookie = cookie,
            UserAgent = userAgent,
        };
    }

    private static string CombineCookies(string rutrackerCookie, string? clearanceCookie)
    {
        string baseCookie = rutrackerCookie.Trim();
        string extra = clearanceCookie?.Trim() ?? string.Empty;

        if (extra.Length == 0)
        {
            return baseCookie;
        }

        // Пользователь мог вставить cf_clearance прямо в основную куку — не дублируем.
        if (baseCookie.Contains("cf_clearance=", StringComparison.OrdinalIgnoreCase))
        {
            return baseCookie;
        }

        return baseCookie.Length == 0 ? extra : baseCookie + "; " + extra;
    }
}
