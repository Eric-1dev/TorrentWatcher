using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TorrentWatcher.Infrastructure.Persistence;

/// <summary>
/// Фабрика для <c>dotnet ef</c>: миграции создаются и накатываются без запуска Web-хоста.
/// Строка подключения берётся из <c>ConnectionStrings__Default</c>, как и в контейнере.
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string DefaultConnectionString = "Data Source=torrentwatcher.db";

    public AppDbContext CreateDbContext(string[] args)
    {
        string connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Default") ?? DefaultConnectionString;

        DbContextOptionsBuilder<AppDbContext> options = new();
        options.UseSqlite(connectionString);
        return new AppDbContext(options.Options);
    }
}
