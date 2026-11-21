using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TorrentWatcher.Application.Exceptions;
using TorrentWatcher.IntegrationTests.Infrastructure;
using TorrentWatcher.IntegrationTests.Support;

namespace TorrentWatcher.IntegrationTests.Api;

public sealed class ApiIntegrationTests
{
    [Fact]
    public async Task GetTorrents_Empty_ReturnsEmptySummary()
    {
        using var app = new TestWebApplicationFactory();
        using HttpClient client = app.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/torrents");
        JsonElement body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, body.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, body.GetProperty("updateAvailableCount").GetInt32());
        Assert.Empty(body.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task PostTorrent_AddsTorrentAndReturnsCreated()
    {
        using var app = new TestWebApplicationFactory();
        using HttpClient client = app.CreateClient();
        await ConfigureCookieAsync(client);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/torrents",
            new { url = "https://rutracker.org/forum/viewtopic.php?t=123456" });
        JsonElement body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/api/torrents/1", response.Headers.Location?.ToString());
        Assert.Equal(123456, body.GetProperty("topicId").GetInt32());
        Assert.Equal("test", body.GetProperty("title").GetString());
        Assert.Equal(40, body.GetProperty("infoHashV1").GetString()!.Length);
        Assert.Equal("Unchanged", body.GetProperty("lastCheckOutcome").GetString());
        Assert.True(body.GetProperty("isEnabled").GetBoolean());

        Assert.Single(app.Rutracker.Requests);
        Assert.True(File.Exists(Path.Combine(app.TorrentsDirectory, "123456.torrent")));

        HttpResponseMessage list = await client.GetAsync("/api/torrents");
        JsonElement listBody = await ReadJsonAsync(list);
        Assert.Equal(1, listBody.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task PostDuplicate_Returns409ProblemDetails()
    {
        using var app = new TestWebApplicationFactory();
        using HttpClient client = app.CreateClient();
        await ConfigureCookieAsync(client);
        await AddTorrentAsync(client);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/torrents",
            new { url = "viewtopic.php?t=123456" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.StartsWith("application/problem+json", response.Content.Headers.ContentType?.ToString());

        JsonElement body = await ReadJsonAsync(response);
        Assert.Equal(ErrorCodes.DuplicateTopic, body.GetProperty("code").GetString());
        Assert.Single(app.Rutracker.Requests);
    }

    [Fact]
    public async Task DeleteTorrent_Returns204AndRemovesFile()
    {
        using var app = new TestWebApplicationFactory();
        using HttpClient client = app.CreateClient();
        await ConfigureCookieAsync(client);
        int id = await AddTorrentAsync(client);

        HttpResponseMessage response = await client.DeleteAsync($"/api/torrents/{id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(File.Exists(Path.Combine(app.TorrentsDirectory, "123456.torrent")));

        JsonElement listBody = await ReadJsonAsync(await client.GetAsync("/api/torrents"));
        Assert.Equal(0, listBody.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task CheckTorrent_UnchangedThenChanged_UpdatesStatus()
    {
        using var app = new TestWebApplicationFactory();
        using HttpClient client = app.CreateClient();
        await ConfigureCookieAsync(client);
        int id = await AddTorrentAsync(client);

        JsonElement unchanged = await ReadJsonAsync(
            await client.PostAsync($"/api/torrents/{id}/check", content: null));

        Assert.False(unchanged.GetProperty("changed").GetBoolean());
        Assert.Equal("Unchanged", unchanged.GetProperty("torrent").GetProperty("lastCheckOutcome").GetString());

        app.Rutracker.Payload = TorrentPayloads.SingleFile("test2");

        JsonElement changed = await ReadJsonAsync(
            await client.PostAsync($"/api/torrents/{id}/check", content: null));

        Assert.True(changed.GetProperty("changed").GetBoolean());
        Assert.Equal("Changed", changed.GetProperty("torrent").GetProperty("lastCheckOutcome").GetString());
        Assert.Equal(
            JsonValueKind.String,
            changed.GetProperty("torrent").GetProperty("lastChangedAtUtc").ValueKind);
    }

    [Fact]
    public async Task UnknownApiEndpoint_Returns404NotIndexHtml()
    {
        using var app = new TestWebApplicationFactory();
        using HttpClient client = app.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/does-not-exist");
        string content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.StartsWith("application/problem+json", response.Content.Headers.ContentType?.ToString());
        Assert.DoesNotContain("<html", content, StringComparison.OrdinalIgnoreCase);

        JsonElement body = JsonDocument.Parse(content).RootElement;
        Assert.Equal(ErrorCodes.EndpointNotFound, body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Settings_PutAppliesImmediatelyAndMasksCookie()
    {
        using var app = new TestWebApplicationFactory();
        using HttpClient client = app.CreateClient();

        HttpResponseMessage put = await client.PutAsJsonAsync("/api/settings", new
        {
            rutrackerCookie = "bb_session=secret9876",
            scanIntervalMinutes = 15,
            autoScanEnabled = false,
            requestDelayMs = 250,
            requestTimeoutSeconds = 10,
        });

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        string raw = await put.Content.ReadAsStringAsync();
        Assert.DoesNotContain("secret9876", raw);

        JsonElement body = JsonDocument.Parse(raw).RootElement;
        Assert.Equal(15, body.GetProperty("scanIntervalMinutes").GetInt32());
        Assert.False(body.GetProperty("autoScanEnabled").GetBoolean());
        Assert.True(body.GetProperty("hasCookie").GetBoolean());
        Assert.EndsWith("9876", body.GetProperty("maskedCookie").GetString());

        JsonElement getBody = await ReadJsonAsync(await client.GetAsync("/api/settings"));
        Assert.Equal(15, getBody.GetProperty("scanIntervalMinutes").GetInt32());
    }

    [Fact]
    public async Task Settings_PutCredentials_StoresLoginAndNeverReturnsPassword()
    {
        using var app = new TestWebApplicationFactory();
        using HttpClient client = app.CreateClient();

        HttpResponseMessage put = await client.PutAsJsonAsync("/api/settings", new
        {
            rutrackerLogin = "user",
            rutrackerPassword = "supersecret",
            scanIntervalMinutes = 15,
            autoScanEnabled = false,
            requestDelayMs = 250,
            requestTimeoutSeconds = 10,
        });

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        string raw = await put.Content.ReadAsStringAsync();
        Assert.DoesNotContain("supersecret", raw);

        JsonElement body = JsonDocument.Parse(raw).RootElement;
        Assert.Equal("user", body.GetProperty("rutrackerLogin").GetString());
        Assert.True(body.GetProperty("hasPassword").GetBoolean());
        Assert.True(body.GetProperty("hasCredentials").GetBoolean());

        string getRaw = await client.GetStringAsync("/api/settings");
        Assert.DoesNotContain("supersecret", getRaw);
    }

    [Fact]
    public async Task Settings_Qbittorrent_StoresPasswordMaskedAndReportsConfigured()
    {
        using var app = new TestWebApplicationFactory();
        using HttpClient client = app.CreateClient();

        HttpResponseMessage put = await client.PutAsJsonAsync("/api/settings", new
        {
            scanIntervalMinutes = 60,
            autoScanEnabled = true,
            requestDelayMs = 1500,
            requestTimeoutSeconds = 30,
            qbittorrentUrl = "http://plex.local:8080/",
            qbittorrentUsername = "admin",
            qbittorrentPassword = "qbsecret",
            qbittorrentAutoApply = true,
        });

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        string raw = await put.Content.ReadAsStringAsync();
        Assert.DoesNotContain("qbsecret", raw);

        JsonElement body = JsonDocument.Parse(raw).RootElement;
        Assert.Equal("http://plex.local:8080", body.GetProperty("qbittorrentUrl").GetString());
        Assert.Equal("admin", body.GetProperty("qbittorrentUsername").GetString());
        Assert.True(body.GetProperty("hasQbittorrentPassword").GetBoolean());
        Assert.True(body.GetProperty("qbittorrentConfigured").GetBoolean());
        Assert.True(body.GetProperty("qbittorrentAutoApply").GetBoolean());

        string getRaw = await client.GetStringAsync("/api/settings");
        Assert.DoesNotContain("qbsecret", getRaw);
    }

    [Fact]
    public async Task Settings_QbittorrentInvalidUrl_Returns400()
    {
        using var app = new TestWebApplicationFactory();
        using HttpClient client = app.CreateClient();

        HttpResponseMessage response = await client.PutAsJsonAsync("/api/settings", new
        {
            scanIntervalMinutes = 60,
            autoScanEnabled = true,
            requestDelayMs = 1500,
            requestTimeoutSeconds = 30,
            qbittorrentUrl = "plex.local:8080",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        JsonElement body = await ReadJsonAsync(response);
        Assert.Equal(ErrorCodes.SettingsInvalid, body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Settings_InvalidInterval_Returns400WithCode()
    {
        using var app = new TestWebApplicationFactory();
        using HttpClient client = app.CreateClient();

        HttpResponseMessage response = await client.PutAsJsonAsync("/api/settings", new
        {
            scanIntervalMinutes = 0,
            autoScanEnabled = true,
            requestDelayMs = 0,
            requestTimeoutSeconds = 30,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        JsonElement body = await ReadJsonAsync(response);
        Assert.Equal(ErrorCodes.SettingsInvalid, body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AddTorrent_WithoutCookie_Returns401AndDoesNotCallRutracker()
    {
        using var app = new TestWebApplicationFactory();
        using HttpClient client = app.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/torrents",
            new { url = "123456" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        JsonElement body = await ReadJsonAsync(response);
        Assert.Equal(ErrorCodes.RutrackerCookieInvalid, body.GetProperty("code").GetString());
        Assert.Empty(app.Rutracker.Requests);
    }

    [Fact]
    public async Task CheckAll_Returns202WithRunId()
    {
        using var app = new TestWebApplicationFactory();
        using HttpClient client = app.CreateClient();
        await ConfigureCookieAsync(client);
        await AddTorrentAsync(client);

        HttpResponseMessage response = await client.PostAsync("/api/torrents/check-all", content: null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        JsonElement body = await ReadJsonAsync(response);
        Assert.True(body.GetProperty("started").GetBoolean());
        Assert.NotEqual(Guid.Empty, body.GetProperty("runId").GetGuid());
    }

    [Fact]
    public async Task Health_ReturnsOk()
    {
        using var app = new TestWebApplicationFactory();
        using HttpClient client = app.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task ConfigureCookieAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.PutAsJsonAsync("/api/settings", new
        {
            rutrackerCookie = "bb_session=secret",
            scanIntervalMinutes = 60,
            autoScanEnabled = true,
            requestDelayMs = 0,
            requestTimeoutSeconds = 30,
        });

        response.EnsureSuccessStatusCode();
    }

    private static async Task<int> AddTorrentAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/torrents",
            new { url = "https://rutracker.org/forum/viewtopic.php?t=123456" });

        response.EnsureSuccessStatusCode();
        JsonElement body = await ReadJsonAsync(response);

        return body.GetProperty("id").GetInt32();
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        string json = await response.Content.ReadAsStringAsync();

        return JsonDocument.Parse(json).RootElement;
    }
}
