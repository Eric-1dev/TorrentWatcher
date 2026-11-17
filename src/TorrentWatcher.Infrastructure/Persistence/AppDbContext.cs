using Microsoft.EntityFrameworkCore;
using TorrentWatcher.Domain.Entities;
using TorrentWatcher.Infrastructure.Persistence.Conversions;

namespace TorrentWatcher.Infrastructure.Persistence;

/// <summary>
/// Единственный DbContext приложения. Конфигурации сущностей — в
/// <c>Persistence/Configurations</c>, миграции — в <c>Persistence/Migrations</c>.
/// </summary>
public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<TrackedTorrent> TrackedTorrents => Set<TrackedTorrent>();

    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        // SQLite не хранит DateTimeKind — при чтении всегда помечаем время как UTC.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
}
