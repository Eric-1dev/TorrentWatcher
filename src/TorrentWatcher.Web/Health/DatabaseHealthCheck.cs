using Microsoft.Extensions.Diagnostics.HealthChecks;
using TorrentWatcher.Infrastructure.Persistence;

namespace TorrentWatcher.Web.Health;

/// <summary>
/// Проверяет, что SQLite доступна. Health check живёт в singleton-сервисе, поэтому
/// DbContext берётся из отдельного scope и не удерживается между запросами.
/// </summary>
public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly IServiceScopeFactory _scopeFactory;

    public DatabaseHealthCheck(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            return await db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy("SQLite доступна.")
                : HealthCheckResult.Unhealthy("SQLite недоступна.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Ошибка подключения к SQLite.", exception);
        }
    }
}
