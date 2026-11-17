using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TorrentWatcher.Application.Abstractions;
using TorrentWatcher.Domain;
using TorrentWatcher.Infrastructure.Persistence;
using TorrentWatcher.Infrastructure.Repositories;
using TorrentWatcher.Infrastructure.Rutracker;
using TorrentWatcher.Infrastructure.Storage;

namespace TorrentWatcher.Infrastructure;

/// <summary>Регистрация инфраструктуры: SQLite, репозитории, PRAGMA-интерсептор, рутрекер, файлы.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string connectionString,
        string torrentsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(torrentsDirectory);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ITorrentFileStorage>(provider =>
            new TorrentFileStorage(torrentsDirectory, provider.GetRequiredService<TimeProvider>()));

        services.AddSingleton<SqlitePragmaInterceptor>();
        services.AddDbContext<AppDbContext>((provider, options) =>
            options
                .UseSqlite(connectionString)
                .AddInterceptors(provider.GetRequiredService<SqlitePragmaInterceptor>()));

        services.AddScoped<ITrackedTorrentRepository, TrackedTorrentRepository>();
        services.AddScoped<IAppSettingsRepository, AppSettingsRepository>();

        services.AddHttpClient<IRutrackerClient, RutrackerClient>(client =>
        {
            client.BaseAddress = new Uri(RutrackerUrls.BaseUrl + "/");

            // Таймаут задаётся на каждый запрос из настроек, поэтому общий отключаем.
            client.Timeout = Timeout.InfiniteTimeSpan;
        });

        return services;
    }
}
