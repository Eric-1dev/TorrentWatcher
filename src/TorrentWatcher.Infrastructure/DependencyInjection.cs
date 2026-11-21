using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TorrentWatcher.Application.Abstractions;
using TorrentWatcher.Application.Rutracker;
using TorrentWatcher.Domain;
using TorrentWatcher.Infrastructure.Persistence;
using TorrentWatcher.Infrastructure.Qbittorrent;
using TorrentWatcher.Infrastructure.Repositories;
using TorrentWatcher.Infrastructure.Rutracker;
using TorrentWatcher.Infrastructure.Security;
using TorrentWatcher.Infrastructure.Storage;

namespace TorrentWatcher.Infrastructure;

/// <summary>Регистрация инфраструктуры: SQLite, репозитории, PRAGMA-интерсептор, рутрекер, файлы.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string connectionString,
        string torrentsDirectory,
        string keysDirectory,
        RutrackerBrowserOptions browserOptions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(torrentsDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(keysDirectory);
        ArgumentNullException.ThrowIfNull(browserOptions);

        services.TryAddSingleton(TimeProvider.System);

        // Пароль рутрекера шифруется ключами Data Protection; ключи — вне БД.
        services
            .AddDataProtection()
            .SetApplicationName("TorrentWatcher")
            .PersistKeysToFileSystem(new DirectoryInfo(keysDirectory));
        services.AddSingleton<ISecretProtector, SecretProtector>();
        services.AddSingleton<ITorrentFileStorage>(provider =>
            new TorrentFileStorage(torrentsDirectory, provider.GetRequiredService<TimeProvider>()));

        services.AddSingleton<SqlitePragmaInterceptor>();
        services.AddDbContext<AppDbContext>((provider, options) =>
            options
                .UseSqlite(connectionString)
                .AddInterceptors(provider.GetRequiredService<SqlitePragmaInterceptor>()));

        services.AddScoped<ITrackedTorrentRepository, TrackedTorrentRepository>();
        services.AddScoped<IAppSettingsRepository, AppSettingsRepository>();

        // Транспорт без Cloudflare: один HTTP-запрос. Обёртка с решением челленджа — в Application.
        services.AddHttpClient<IRutrackerHttpClient, RutrackerClient>(client =>
        {
            client.BaseAddress = new Uri(RutrackerUrls.BaseUrl + "/");

            // Таймаут задаётся на каждый запрос из настроек, поэтому общий отключаем.
            client.Timeout = Timeout.InfiniteTimeSpan;
        });

        // Переключение «HTTP → браузер» живёт весь процесс, а не один scoped-клиент.
        services.AddSingleton<CloudflareTransportState>();
        services.AddScoped<IRutrackerClient, CloudflareAwareRutrackerClient>();

        // qBittorrent: адрес приходит из настроек, поэтому BaseAddress не задаём.
        // Таймаут — на каждый запрос из настроек, общий отключаем.
        services.AddHttpClient<ITorrentClient, QbittorrentClient>(client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
        });

        // Браузер-решатель челленджа и загрузчик .torrent: singleton, профиль на диске,
        // запуск по требованию. Один экземпляр на два порта — общий Chromium и профиль.
        services.AddSingleton(browserOptions);
        services.AddSingleton<PlaywrightChallengeSolver>();
        services.AddSingleton<ICloudflareChallengeSolver>(
            provider => provider.GetRequiredService<PlaywrightChallengeSolver>());
        services.AddSingleton<IBrowserTorrentDownloader>(
            provider => provider.GetRequiredService<PlaywrightChallengeSolver>());
        services.AddSingleton<IRutrackerLogin>(
            provider => provider.GetRequiredService<PlaywrightChallengeSolver>());

        return services;
    }
}
