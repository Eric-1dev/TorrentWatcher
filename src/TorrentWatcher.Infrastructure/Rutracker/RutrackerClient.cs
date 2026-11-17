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
/// rutracker.org, подставляет браузерный User-Agent, Referer на топик и куку из настроек.
/// Ответ проверяется на признаки .torrent до того, как попадёт в хранилище.
/// </summary>
public sealed class RutrackerClient : IRutrackerClient
{
    /// <summary>Защита от гигантских ответов: настоящий .torrent заметно меньше.</summary>
    public const int MaxTorrentBytes = 10 * 1024 * 1024;

    private const string UserAgentValue =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
        + "(KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    private static readonly byte[] s_announceMarker = Encoding.ASCII.GetBytes("8:announce");

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

            httpRequest.Headers.TryAddWithoutValidation("User-Agent", UserAgentValue);
            httpRequest.Headers.Referrer = new Uri(RutrackerUrls.Topic(request.TopicId));

            if (!string.IsNullOrWhiteSpace(request.Cookie))
            {
                httpRequest.Headers.TryAddWithoutValidation("Cookie", request.Cookie);
            }

            using HttpResponseMessage response = await _httpClient.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutSource.Token);

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

    private static void EnsureTorrentPayload(ReadOnlySpan<byte> content)
    {
        if (content.IsEmpty)
        {
            throw new RutrackerException(
                RutrackerFailureKind.InvalidPayload,
                "Рутрекер вернул пустой ответ вместо .torrent.");
        }

        if (content[0] != (byte)'d')
        {
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
}
