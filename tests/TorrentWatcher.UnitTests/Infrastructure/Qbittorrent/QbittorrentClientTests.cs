using System.Net;
using TorrentWatcher.Application.Exceptions;
using TorrentWatcher.Application.Services;
using TorrentWatcher.Application.Settings;
using TorrentWatcher.Application.TorrentClients;
using TorrentWatcher.Domain.Entities;
using TorrentWatcher.Infrastructure.Qbittorrent;
using TorrentWatcher.UnitTests.Infrastructure.Rutracker;
using TorrentWatcher.UnitTests.Support;

namespace TorrentWatcher.UnitTests.Infrastructure.Qbittorrent;

public sealed class QbittorrentClientTests
{
    private const string BaseUrl = "http://plex.local:8080";

    [Fact]
    public async Task FindAsync_LogsInAndReturnsSavePathCategoryAndTags()
    {
        string? cookieHeader = null;

        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            string path = request.RequestUri!.AbsolutePath;

            if (path.EndsWith("auth/login", StringComparison.Ordinal))
            {
                string body = await request.Content!.ReadAsStringAsync(cancellationToken);
                Assert.Contains("username=admin", body);
                Assert.Contains("password=secret", body);

                return LoginOk();
            }

            if (path.EndsWith("torrents/info", StringComparison.Ordinal))
            {
                cookieHeader = request.Headers.TryGetValues("Cookie", out var values)
                    ? string.Join(";", values)
                    : null;

                return Json(
                    """
                    [{"hash":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","name":"Раздача",
                      "save_path":"/downloads/Раздача","category":"movies","tags":"tag1, tag2",
                      "state":"uploading"}]
                    """);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        TorrentClientTorrent? torrent = await CreateClient(handler)
            .FindAsync("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", CancellationToken.None);

        Assert.NotNull(torrent);
        Assert.Equal("/downloads/Раздача", torrent.SavePath);
        Assert.Equal("movies", torrent.Category);
        Assert.Equal("tag1, tag2", torrent.Tags);
        Assert.Equal("uploading", torrent.State);
        Assert.Equal("SID=abc", cookieHeader);
    }

    [Fact]
    public async Task FindAsync_NoMatches_ReturnsNull()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            string path = request.RequestUri!.AbsolutePath;

            return Task.FromResult(path.EndsWith("auth/login", StringComparison.Ordinal)
                ? LoginOk()
                : Json("[]"));
        });

        TorrentClientTorrent? torrent = await CreateClient(handler)
            .FindAsync(new string('b', 40), CancellationToken.None);

        Assert.Null(torrent);
    }

    [Fact]
    public async Task FindByNameAsync_ReturnsOnlyTorrentsWithSameName()
    {
        string? query = null;

        var handler = new StubHttpMessageHandler((request, _) =>
        {
            string path = request.RequestUri!.AbsolutePath;

            if (path.EndsWith("auth/login", StringComparison.Ordinal))
            {
                return Task.FromResult(LoginOk());
            }

            query = request.RequestUri.Query;

            return Task.FromResult(Json(
                """
                [{"hash":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","name":"Раздача",
                  "save_path":"/downloads/Раздача","category":"movies","tags":"tag1","state":"uploading"},
                 {"hash":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","name":"Другая раздача",
                  "save_path":"/downloads/Другая","category":"","tags":"","state":"stalledUP"},
                 {"hash":"cccccccccccccccccccccccccccccccccccccccc","name":"раздача",
                  "save_path":"/downloads/Раздача","category":"","tags":"","state":"uploading"}]
                """));
        });

        IReadOnlyList<TorrentClientTorrent> torrents = await CreateClient(handler)
            .FindByNameAsync("Раздача", CancellationToken.None);

        Assert.Empty(query ?? string.Empty);
        Assert.Equal(2, torrents.Count);
        Assert.All(torrents, torrent => Assert.Equal("Раздача", torrent.Name, ignoreCase: true));
        Assert.Contains(torrents, torrent => torrent.Category == "movies");
    }

    [Fact]
    public async Task GetVersionAsync_RejectedLogin_ThrowsAuthFailed()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("Fails."),
            }));

        AppException exception = await Assert.ThrowsAsync<AppException>(
            () => CreateClient(handler).GetVersionAsync(CancellationToken.None));

        Assert.Equal(ErrorCodes.QbittorrentAuthFailed, exception.Code);
    }

    [Fact]
    public async Task GetVersionAsync_Success_ReturnsVersion()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            string path = request.RequestUri!.AbsolutePath;

            return Task.FromResult(path.EndsWith("auth/login", StringComparison.Ordinal)
                ? LoginOk()
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("v5.0.3") });
        });

        string version = await CreateClient(handler).GetVersionAsync(CancellationToken.None);

        Assert.Equal("v5.0.3", version);
    }

    [Fact]
    public async Task AddTorrentAsync_SendsFileSavePathAndAutoTmmDisabled()
    {
        string torrentFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(torrentFile, "d4:infod4:name1:xee");
        string? addBody = null;

        try
        {
            var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
            {
                string path = request.RequestUri!.AbsolutePath;

                if (path.EndsWith("auth/login", StringComparison.Ordinal))
                {
                    return LoginOk();
                }

                if (path.EndsWith("torrents/add", StringComparison.Ordinal))
                {
                    addBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Ok.") };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            await CreateClient(handler).AddTorrentAsync(
                new TorrentClientAddRequest(
                    TopicId: 42,
                    Title: "Раздача",
                    InfoHashV1: new string('c', 40),
                    TorrentFilePath: torrentFile,
                    SavePath: "/downloads/Раздача",
                    Category: "movies",
                    Tags: "tag1"),
                CancellationToken.None);
        }
        finally
        {
            File.Delete(torrentFile);
        }

        Assert.NotNull(addBody);
        Assert.Contains("name=savepath", addBody);
        Assert.Contains("/downloads/Раздача", addBody);
        Assert.Contains("name=autoTMM", addBody);
        Assert.Contains("name=category", addBody);
        Assert.Contains("name=tags", addBody);
    }

    [Fact]
    public async Task AddTorrentAsync_ResponseFails_ThrowsUnavailable()
    {
        string torrentFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(torrentFile, "d4:infod4:name1:xee");

        try
        {
            var handler = new StubHttpMessageHandler((request, _) =>
            {
                string path = request.RequestUri!.AbsolutePath;

                return Task.FromResult(path.EndsWith("auth/login", StringComparison.Ordinal)
                    ? LoginOk()
                    : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Fails.") });
            });

            AppException exception = await Assert.ThrowsAsync<AppException>(
                () => CreateClient(handler).AddTorrentAsync(
                    new TorrentClientAddRequest(1, "t", new string('d', 40), torrentFile),
                    CancellationToken.None));

            Assert.Equal(ErrorCodes.QbittorrentUnavailable, exception.Code);
        }
        finally
        {
            File.Delete(torrentFile);
        }
    }

    [Fact]
    public async Task DeleteTorrentAsync_KeepsFiles()
    {
        string? deleteBody = null;

        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            string path = request.RequestUri!.AbsolutePath;

            if (path.EndsWith("auth/login", StringComparison.Ordinal))
            {
                return LoginOk();
            }

            if (path.EndsWith("torrents/delete", StringComparison.Ordinal))
            {
                deleteBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        await CreateClient(handler).DeleteTorrentAsync(new string('e', 40), CancellationToken.None);

        Assert.NotNull(deleteBody);
        Assert.Contains("hashes=eeee", deleteBody);
        Assert.Contains("deleteFiles=false", deleteBody);
    }

    private static QbittorrentClient CreateClient(HttpMessageHandler handler) =>
        new(new HttpClient(handler), CreateSettings(), new StubSecretProtector());

    private static SettingsProvider CreateSettings()
    {
        var provider = new SettingsProvider();
        provider.Apply(AppSettingsSnapshot.FromEntity(new AppSetting
        {
            QbittorrentUrl = BaseUrl,
            QbittorrentUsername = "admin",
            QbittorrentPasswordProtected = "enc:secret",
            QbittorrentAutoApply = true,
            RequestTimeoutSeconds = 5,
            UpdatedAtUtc = DateTime.UtcNow,
        }));

        return provider;
    }

    private static HttpResponseMessage LoginOk()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("Ok."),
        };

        response.Headers.TryAddWithoutValidation("Set-Cookie", "SID=abc; HttpOnly; path=/");
        return response;
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json) };
}
