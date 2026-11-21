using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TorrentWatcher.Application.Abstractions;
using TorrentWatcher.IntegrationTests.Support;

namespace TorrentWatcher.IntegrationTests.Infrastructure;

/// <summary>
/// Поднимает приложение на временной SQLite и файловом каталоге. Клиент рутрекера
/// заменён на <see cref="FakeRutrackerClient"/>, поэтому тесты не ходят в сеть.
/// </summary>
public sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _rootDirectory = Path.Combine(
        Path.GetTempPath(),
        "torrentwatcher-integration",
        Guid.NewGuid().ToString("N"));

    public FakeRutrackerClient Rutracker { get; } = new();

    public TestWebApplicationFactory()
    {
        Directory.CreateDirectory(_rootDirectory);
    }

    public string RootDirectory => _rootDirectory;

    public string DatabasePath => Path.Combine(_rootDirectory, "torrentwatcher.db");

    public string TorrentsDirectory => Path.Combine(_rootDirectory, "torrents");

    public string KeysDirectory => Path.Combine(_rootDirectory, "keys");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={DatabasePath}");
        builder.UseSetting("Storage:TorrentsDirectory", TorrentsDirectory);
        builder.UseSetting("Security:KeysDirectory", KeysDirectory);

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IRutrackerClient>();
            services.AddSingleton<IRutrackerClient>(Rutracker);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        try
        {
            if (Directory.Exists(_rootDirectory))
            {
                Directory.Delete(_rootDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Временный каталог подчистит ОС.
        }
    }
}
