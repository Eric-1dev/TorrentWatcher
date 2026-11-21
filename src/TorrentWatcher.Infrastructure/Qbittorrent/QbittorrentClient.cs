using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using TorrentWatcher.Application.Abstractions;
using TorrentWatcher.Application.Exceptions;
using TorrentWatcher.Application.Settings;
using TorrentWatcher.Application.TorrentClients;

namespace TorrentWatcher.Infrastructure.Qbittorrent;

/// <summary>
/// Клиент Web API qBittorrent (API v2). Настройки читаются из снапшота на каждый вызов,
/// поэтому смена адреса или пароля применяется без перезапуска. Логин выполняется перед
/// каждой операцией: приложение делает редкие пакетные вызовы, кэш сеанса не нужен.
/// </summary>
public sealed class QbittorrentClient : ITorrentClient
{
    private const string ApiPrefix = "api/v2/";
    private const int MinTimeoutSeconds = 1;
    private const int MaxTimeoutSeconds = 600;

    private readonly HttpClient _http;
    private readonly ISettingsProvider _settings;
    private readonly ISecretProtector _protector;

    public QbittorrentClient(
        HttpClient http,
        ISettingsProvider settings,
        ISecretProtector protector)
    {
        _http = http;
        _settings = settings;
        _protector = protector;
    }

    public async Task<string> GetVersionAsync(CancellationToken cancellationToken)
    {
        Connection connection = await LoginAsync(cancellationToken);
        ApiResponse response = await SendAsync(
            HttpMethod.Get,
            BuildUri(connection, "app/version"),
            contentFactory: null,
            connection,
            cancellationToken);

        string version = response.Body.Trim();
        return version.Length == 0 ? "unknown" : version;
    }

    public async Task<TorrentClientTorrent?> FindAsync(
        string infoHash,
        CancellationToken cancellationToken)
    {
        Connection connection = await LoginAsync(cancellationToken);
        ApiResponse response = await SendAsync(
            HttpMethod.Get,
            BuildUri(connection, $"torrents/info?hashes={Uri.EscapeDataString(infoHash)}"),
            contentFactory: null,
            connection,
            cancellationToken);

        TorrentInfoDto? torrent = ReadTorrentList(response.Body).FirstOrDefault();
        return torrent is null ? null : ToTorrentClientTorrent(torrent, infoHash);
    }

    public async Task<IReadOnlyList<TorrentClientTorrent>> FindByNameAsync(
        string name,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Connection connection = await LoginAsync(cancellationToken);
        ApiResponse response = await SendAsync(
            HttpMethod.Get,
            BuildUri(connection, "torrents/info"),
            contentFactory: null,
            connection,
            cancellationToken);

        // Сравнение без учёта регистра: название приходит из info-словаря .torrent,
        // а клиент хранит его как есть. Полное совпадение исключает чужие раздачи.
        return ReadTorrentList(response.Body)
            .Where(torrent => string.Equals(torrent.Name, name, StringComparison.OrdinalIgnoreCase))
            .Select(torrent => ToTorrentClientTorrent(torrent, fallbackInfoHash: string.Empty))
            .ToArray();
    }

    public async Task AddTorrentAsync(
        TorrentClientAddRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!File.Exists(request.TorrentFilePath))
        {
            throw new AppException(
                ErrorCodes.StorageFailure,
                $"Файл .torrent не найден: {request.TorrentFilePath}.");
        }

        Connection connection = await LoginAsync(cancellationToken);

        ApiResponse response;
        try
        {
            response = await SendAsync(
                HttpMethod.Post,
                BuildUri(connection, "torrents/add"),
                BuildAddContent(request),
                connection,
                cancellationToken);
        }
        catch (IOException exception)
        {
            throw new AppException(
                ErrorCodes.StorageFailure,
                $"Не удалось прочитать .torrent: {exception.Message}",
                exception);
        }

        if (!response.Body.TrimStart().StartsWith("Ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new AppException(
                ErrorCodes.QbittorrentUnavailable,
                $"qBittorrent не принял торрент: {Shorten(response.Body)}");
        }
    }

    public async Task DeleteTorrentAsync(string infoHash, CancellationToken cancellationToken)
    {
        Connection connection = await LoginAsync(cancellationToken);

        await SendAsync(
            HttpMethod.Post,
            BuildUri(connection, "torrents/delete"),
            () => new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("hashes", infoHash),
                new KeyValuePair<string, string>("deleteFiles", "false"),
            ]),
            connection,
            cancellationToken);
    }

    public Task RecheckAsync(string infoHash, CancellationToken cancellationToken) =>
        PostHashesAsync("torrents/recheck", infoHash, cancellationToken);

    public Task ResumeAsync(string infoHash, CancellationToken cancellationToken) =>
        PostHashesAsync("torrents/resume", infoHash, cancellationToken);

    private async Task PostHashesAsync(
        string relativePath,
        string infoHash,
        CancellationToken cancellationToken)
    {
        Connection connection = await LoginAsync(cancellationToken);

        await SendAsync(
            HttpMethod.Post,
            BuildUri(connection, relativePath),
            () => new FormUrlEncodedContent(
                [new KeyValuePair<string, string>("hashes", infoHash)]),
            connection,
            cancellationToken);
    }

    private async Task<Connection> LoginAsync(CancellationToken cancellationToken)
    {
        AppSettingsSnapshot settings = _settings.Current;

        if (!settings.QbittorrentConfigured)
        {
            throw new AppException(
                ErrorCodes.QbittorrentNotConfigured,
                "qBittorrent не настроен: укажите адрес Web UI, логин и пароль в настройках.");
        }

        string? password = _protector.Unprotect(settings.QbittorrentPasswordProtected);
        if (password is null)
        {
            throw new AppException(
                ErrorCodes.QbittorrentAuthFailed,
                "Пароль qBittorrent не удалось расшифровать: возможно, потерян каталог ключей. "
                + "Введите пароль заново в настройках.");
        }

        var pending = new Connection(
            ParseBaseUri(settings.QbittorrentUrl),
            SessionCookie: null,
            ClampTimeoutSeconds(settings.RequestTimeoutSeconds));

        ApiResponse response = await SendAsync(
            HttpMethod.Post,
            BuildUri(pending, "auth/login"),
            () => new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("username", settings.QbittorrentUsername),
                new KeyValuePair<string, string>("password", password),
            ]),
            pending,
            cancellationToken);

        if (!response.Body.TrimStart().StartsWith("Ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new AppException(
                ErrorCodes.QbittorrentAuthFailed,
                "qBittorrent отклонил логин или пароль.");
        }

        return pending with { SessionCookie = response.SessionCookie };
    }

    /// <summary>
    /// Один запрос к API: логин уже выполнен, кука сеанса подставляется заголовком.
    /// Сетевые сбои и таймаут превращаются в <see cref="AppException"/> с машинным кодом.
    /// </summary>
    private async Task<ApiResponse> SendAsync(
        HttpMethod method,
        Uri uri,
        Func<HttpContent>? contentFactory,
        Connection connection,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(connection.TimeoutSeconds));

        try
        {
            using var request = new HttpRequestMessage(method, uri);

            if (contentFactory is not null)
            {
                request.Content = contentFactory();
            }

            if (!string.IsNullOrEmpty(connection.SessionCookie))
            {
                request.Headers.TryAddWithoutValidation("Cookie", connection.SessionCookie);
            }

            using HttpResponseMessage response = await _http.SendAsync(request, timeout.Token);
            string body = await response.Content.ReadAsStringAsync(timeout.Token);

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                throw new AppException(
                    ErrorCodes.QbittorrentAuthFailed,
                    "qBittorrent отклонил запрос (403). Проверьте логин, пароль и настройку "
                    + "«Enable Host header validation» в Web UI.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new AppException(
                    ErrorCodes.QbittorrentUnavailable,
                    $"qBittorrent ответил HTTP {(int)response.StatusCode}.");
            }

            return new ApiResponse(body, ReadSessionCookie(response));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AppException(
                ErrorCodes.QbittorrentUnavailable,
                $"qBittorrent не ответил за {connection.TimeoutSeconds} с. "
                + "Проверьте адрес, доступность порта и настройку Web UI.");
        }
        catch (HttpRequestException exception)
        {
            throw new AppException(
                ErrorCodes.QbittorrentUnavailable,
                $"Не удалось подключиться к qBittorrent: {exception.Message}",
                exception);
        }
    }

    private static Func<HttpContent> BuildAddContent(TorrentClientAddRequest request) => () =>
    {
        var multipart = new MultipartFormDataContent();

        try
        {
            var file = new StreamContent(File.OpenRead(request.TorrentFilePath));
            file.Headers.ContentType = new MediaTypeHeaderValue("application/x-bittorrent");
            multipart.Add(file, "torrents", $"{request.TopicId}.torrent");

            if (!string.IsNullOrWhiteSpace(request.SavePath))
            {
                // SavePath действует только при выключенном автоматическом управлении папкой.
                multipart.Add(new StringContent(request.SavePath), "savepath");
                multipart.Add(new StringContent("false"), "autoTMM");
            }

            if (!string.IsNullOrWhiteSpace(request.Category))
            {
                multipart.Add(new StringContent(request.Category), "category");
            }

            if (!string.IsNullOrWhiteSpace(request.Tags))
            {
                multipart.Add(new StringContent(request.Tags), "tags");
            }

            multipart.Add(new StringContent(request.Paused ? "true" : "false"), "paused");
            return multipart;
        }
        catch
        {
            multipart.Dispose();
            throw;
        }
    };

    private static string? ReadSessionCookie(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            return null;
        }

        foreach (string value in values)
        {
            int separator = value.IndexOf(';', StringComparison.Ordinal);
            string pair = separator >= 0 ? value[..separator] : value;

            if (pair.StartsWith("SID=", StringComparison.Ordinal))
            {
                return pair;
            }
        }

        return null;
    }

    private static Uri ParseBaseUri(string url)
    {
        string normalized = url.Trim().TrimEnd('/') + "/";

        if (Uri.TryCreate(normalized, UriKind.Absolute, out Uri? uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return uri;
        }

        throw new AppException(
            ErrorCodes.QbittorrentNotConfigured,
            "Адрес qBittorrent задан неверно: нужен http(s)-адрес Web UI.");
    }

    private static Uri BuildUri(Connection connection, string relativePath) =>
        new(connection.BaseUri, ApiPrefix + relativePath);

    private static int ClampTimeoutSeconds(int seconds) =>
        Math.Clamp(seconds, MinTimeoutSeconds, MaxTimeoutSeconds);

    private static string Shorten(string value)
    {
        string trimmed = value.Trim();
        return trimmed.Length <= 200 ? trimmed : trimmed[..200] + "…";
    }

    private static TorrentClientTorrent ToTorrentClientTorrent(
        TorrentInfoDto torrent,
        string fallbackInfoHash) =>
        new(
            !string.IsNullOrEmpty(torrent.Hash) ? torrent.Hash : fallbackInfoHash,
            torrent.Name ?? string.Empty,
            torrent.SavePath ?? string.Empty,
            torrent.Category ?? string.Empty,
            torrent.Tags ?? string.Empty,
            torrent.State ?? string.Empty);

    private static List<TorrentInfoDto> ReadTorrentList(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<List<TorrentInfoDto>>(body) ?? [];
        }
        catch (JsonException exception)
        {
            throw new AppException(
                ErrorCodes.QbittorrentUnavailable,
                "qBittorrent вернул неожиданный ответ вместо списка раздач.",
                exception);
        }
    }

    private sealed record Connection(Uri BaseUri, string? SessionCookie, int TimeoutSeconds);

    private sealed record ApiResponse(string Body, string? SessionCookie);

    private sealed class TorrentInfoDto
    {
        [JsonPropertyName("hash")]
        public string? Hash { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("save_path")]
        public string? SavePath { get; set; }

        [JsonPropertyName("category")]
        public string? Category { get; set; }

        [JsonPropertyName("tags")]
        public string? Tags { get; set; }

        [JsonPropertyName("state")]
        public string? State { get; set; }
    }
}
