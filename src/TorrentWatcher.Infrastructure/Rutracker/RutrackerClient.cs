using System.Net;
using System.Text;
using TorrentWatcher.Application.Abstractions;
using TorrentWatcher.Application.Exceptions;
using TorrentWatcher.Application.Rutracker;
using TorrentWatcher.Domain;
using TorrentWatcher.Domain.Entities;

namespace TorrentWatcher.Infrastructure.Rutracker;

/// <summary>
/// Скачивает .torrent по неизменной ссылке <c>dl.php?t={topicId}</c>. Ходит только на
/// rutracker.org, подставляет браузерный User-Agent (или тот, под которым получен пропуск
/// Cloudflare), Referer на топик и куку из настроек. Ответ проверяется на признаки .torrent
/// до того, как попадёт в хранилище; страница челленджа Cloudflare распознаётся отдельно.
/// </summary>
public sealed class RutrackerClient : IRutrackerHttpClient
{
    /// <summary>Защита от гигантских ответов: настоящий .torrent заметно меньше.</summary>
    public const int MaxTorrentBytes = 10 * 1024 * 1024;

    private static readonly byte[] s_announceMarker = Encoding.ASCII.GetBytes("8:announce");

    // Маркеры страницы managed challenge Cloudflare. Проверяем по телу, потому что
    // Cloudflare умеет отдавать интерстишл и с кодом 200.
    private static readonly byte[][] s_challengeMarkers =
    [
        Encoding.ASCII.GetBytes("cf_chl_opt"),
        Encoding.ASCII.GetBytes("challenge-platform"),
        Encoding.ASCII.GetBytes("__cf_chl"),
    ];

    private readonly HttpClient _httpClient;

    public RutrackerClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<byte[]> DownloadTorrentAsync(
        RutrackerDownloadRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.TopicId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.TopicId,
                "Идентификатор топика должен быть положительным.");
        }

        TimeSpan timeout = request.Timeout > TimeSpan.Zero
            ? request.Timeout
            : TimeSpan.FromSeconds(AppSetting.DefaultRequestTimeoutSeconds);

        // HttpClient.Timeout один на клиент, а настройка меняется на лету — таймаут задаём на запрос.
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Get,
                RutrackerUrls.Download(request.TopicId));

            string userAgent = string.IsNullOrWhiteSpace(request.UserAgent)
                ? RutrackerDefaults.UserAgent
                : request.UserAgent;

            httpRequest.Headers.TryAddWithoutValidation("User-Agent", userAgent);
            httpRequest.Headers.TryAddWithoutValidation("Accept-Language", "ru-RU,ru;q=0.9,en;q=0.8");
            httpRequest.Headers.Referrer = new Uri(RutrackerUrls.Topic(request.TopicId));

            if (!string.IsNullOrWhiteSpace(request.Cookie))
            {
                httpRequest.Headers.TryAddWithoutValidation("Cookie", request.Cookie);
            }

            using HttpResponseMessage response = await _httpClient.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutSource.Token);

            EnsureNotChallenge(response);

            EnsureSuccessStatusCode(response, request.TopicId);

            long? declaredLength = response.Content.Headers.ContentLength;
            if (declaredLength > MaxTorrentBytes)
            {
                throw new RutrackerException(
                    RutrackerFailureKind.InvalidPayload,
                    $"Файл .torrent слишком большой: {declaredLength} байт, лимит — {MaxTorrentBytes}.");
            }

            byte[] content = await ReadBodyAsync(response, timeoutSource.Token);
            EnsureTorrentPayload(content);

            return content;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new RutrackerException(
                RutrackerFailureKind.Network,
                $"Рутрекер не ответил за {timeout.TotalSeconds:0.#} с.",
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw new RutrackerException(
                RutrackerFailureKind.Network,
                "Не удалось соединиться с рутрекером.",
                exception);
        }
        catch (IOException exception)
        {
            throw new RutrackerException(
                RutrackerFailureKind.Network,
                "Соединение с рутрекером прервалось при чтении .torrent.",
                exception);
        }
    }

    /// <summary>
    /// Cloudflare помечает managed challenge заголовком <c>Cf-Mitigated: challenge</c>.
    /// Такой ответ нельзя путать с 403 от рутрекера: кука тут ни при чём.
    /// </summary>
    private static void EnsureNotChallenge(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Cf-Mitigated", out IEnumerable<string>? values))
        {
            return;
        }

        if (values.Any(value => value.Contains("challenge", StringComparison.OrdinalIgnoreCase)))
        {
            throw ChallengeException();
        }
    }

    private static void EnsureSuccessStatusCode(HttpResponseMessage response, int topicId)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        switch (response.StatusCode)
        {
            case HttpStatusCode.Unauthorized:
            case HttpStatusCode.Forbidden:
                throw new RutrackerException(
                    RutrackerFailureKind.CookieInvalid,
                    "Рутрекер отклонил куку (HTTP 403). Проверьте куку в настройках.");

            case HttpStatusCode.NotFound:
            case HttpStatusCode.Gone:
                throw new RutrackerException(
                    RutrackerFailureKind.NotFound,
                    $"Топик {topicId} не найден на рутрекере (HTTP {(int)response.StatusCode}).");

            default:
                throw new RutrackerException(
                    RutrackerFailureKind.Network,
                    $"Рутрекер ответил HTTP {(int)response.StatusCode}.");
        }
    }

    private static async Task<byte[]> ReadBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);

        using var buffer = new MemoryStream();
        byte[] chunk = new byte[81_920];

        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxTorrentBytes)
            {
                throw new RutrackerException(
                    RutrackerFailureKind.InvalidPayload,
                    $"Файл .torrent больше лимита {MaxTorrentBytes} байт.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    internal static void EnsureTorrentPayload(ReadOnlySpan<byte> content)
    {
        if (content.IsEmpty)
        {
            throw new RutrackerException(
                RutrackerFailureKind.InvalidPayload,
                "Рутрекер вернул пустой ответ вместо .torrent.");
        }

        if (content[0] != (byte)'d')
        {
            if (LooksLikeCloudflareChallenge(content))
            {
                throw ChallengeException();
            }

            throw new RutrackerException(
                RutrackerFailureKind.InvalidPayload,
                "Рутрекер вернул не .torrent: ответ не является bencode-словарём. "
                + "Вероятно, кука недействительна или рутрекер показывает капчу.");
        }

        if (content.IndexOf(s_announceMarker) < 0)
        {
            throw new RutrackerException(
                RutrackerFailureKind.InvalidPayload,
                "Рутрекер вернул bencode без announce — это не .torrent.");
        }
    }

    internal static bool LooksLikeCloudflareChallenge(ReadOnlySpan<byte> content)
    {
        foreach (byte[] marker in s_challengeMarkers)
        {
            if (content.IndexOf(marker) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static RutrackerException ChallengeException() =>
        new(
            RutrackerFailureKind.Challenge,
            "Cloudflare требует проверки браузера: рутрекер вернул страницу проверки "
            + "вместо .torrent. Откройте настройки и нажмите «Пройти проверку Cloudflare».");
}
