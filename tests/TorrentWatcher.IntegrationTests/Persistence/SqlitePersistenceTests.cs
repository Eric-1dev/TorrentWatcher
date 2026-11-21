using System.Data.Common;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TorrentWatcher.Domain.Entities;
using TorrentWatcher.Infrastructure.Persistence;
using TorrentWatcher.Infrastructure.Repositories;

namespace TorrentWatcher.IntegrationTests.Persistence;

/// <summary>
/// Проверки SQLite-слоя на настоящем файле: миграция, PRAGMA, сохранение данных
/// после пересоздания контекста (имитация перезапуска приложения).
/// </summary>
public sealed class SqlitePersistenceTests : IDisposable
{
    private readonly string _directory;
    private readonly string _databasePath;

    public SqlitePersistenceTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "torrentwatcher-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _databasePath = Path.Combine(_directory, "torrentwatcher.db");
    }

    [Fact]
    public async Task TrackedTorrent_SurvivesContextRecreation()
    {
        await using (AppDbContext db = CreateContext())
        {
            await db.Database.MigrateAsync();
            TrackedTorrentRepository repository = new(db);
            await repository.AddAsync(NewTorrent(42, "Первый торрент"), CancellationToken.None);
        }

        await using (AppDbContext db = CreateContext())
        {
            TrackedTorrentRepository repository = new(db);

            TrackedTorrent? torrent = await repository.GetByTopicIdAsync(42, CancellationToken.None);

            Assert.NotNull(torrent);
            Assert.Equal("Первый торрент", torrent.Title);
            Assert.Equal(TorrentCheckOutcome.Never, torrent.LastCheckOutcome);
            Assert.True(torrent.IsEnabled);
        }
    }

    [Fact]
    public async Task Settings_AreSeededAndUpdated()
    {
        await using (AppDbContext db = CreateContext())
        {
            await db.Database.MigrateAsync();
            AppSettingsRepository repository = new(db);

            AppSetting? seeded = await repository.FindAsync(CancellationToken.None);
            Assert.NotNull(seeded);
            Assert.Equal(AppSetting.DefaultScanIntervalMinutes, seeded.ScanIntervalMinutes);
            Assert.Equal(AppSetting.DefaultRequestDelayMs, seeded.RequestDelayMs);
            Assert.True(seeded.AutoScanEnabled);

            seeded.ScanIntervalMinutes = 240;
            seeded.AutoScanEnabled = false;
            await repository.SaveAsync(seeded, CancellationToken.None);
        }

        await using (AppDbContext db = CreateContext())
        {
            AppSettingsRepository repository = new(db);

            AppSetting? reloaded = await repository.FindAsync(CancellationToken.None);
            Assert.NotNull(reloaded);

            Assert.Equal(240, reloaded.ScanIntervalMinutes);
            Assert.False(reloaded.AutoScanEnabled);
            Assert.Equal(DateTimeKind.Utc, reloaded.UpdatedAtUtc.Kind);
        }
    }

    [Fact]
    public async Task Settings_TwoSavesInOneContext_DoNotConflict()
    {
        await using AppDbContext db = CreateContext();
        await db.Database.MigrateAsync();
        AppSettingsRepository repository = new(db);

        AppSetting first = (await repository.FindAsync(CancellationToken.None))!;
        first.ScanIntervalMinutes = 120;
        await repository.SaveAsync(first, CancellationToken.None);

        // Второй слой (например, сохранение пропуска Cloudflare) читает настройки
        // заново в том же scope и сохраняет их.
        AppSetting second = (await repository.FindAsync(CancellationToken.None))!;
        second.CloudflareLastError = "тест";
        await repository.SaveAsync(second, CancellationToken.None);

        AppSetting? reloaded = await repository.FindAsync(CancellationToken.None);

        Assert.NotNull(reloaded);
        Assert.Equal(120, reloaded.ScanIntervalMinutes);
        Assert.Equal("тест", reloaded.CloudflareLastError);
    }

    [Fact]
    public async Task Connection_HasWalBusyTimeoutAndForeignKeys()
    {
        await using AppDbContext db = CreateContext();
        await db.Database.MigrateAsync();

        await db.Database.CloseConnectionAsync();
        await db.Database.OpenConnectionAsync();
        DbConnection connection = db.Database.GetDbConnection();

        Assert.Equal("wal", await ScalarAsync(connection, "PRAGMA journal_mode;"));
        Assert.Equal(SqlitePragmaInterceptor.BusyTimeoutMs.ToString(CultureInfo.InvariantCulture),
            await ScalarAsync(connection, "PRAGMA busy_timeout;"));
        Assert.Equal("1", await ScalarAsync(connection, "PRAGMA foreign_keys;"));
    }

    [Fact]
    public async Task DuplicateTopicId_IsRejected()
    {
        await using AppDbContext db = CreateContext();
        await db.Database.MigrateAsync();
        TrackedTorrentRepository repository = new(db);
        await repository.AddAsync(NewTorrent(7, "Первый"), CancellationToken.None);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => repository.AddAsync(NewTorrent(7, "Дубль"), CancellationToken.None));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private AppDbContext CreateContext()
    {
        DbContextOptionsBuilder<AppDbContext> options = new();
        options
            .UseSqlite($"Data Source={_databasePath}")
            .AddInterceptors(new SqlitePragmaInterceptor());
        return new AppDbContext(options.Options);
    }

    private static TrackedTorrent NewTorrent(int topicId, string title) =>
        new()
        {
            TopicId = topicId,
            TopicUrl = $"https://rutracker.org/forum/viewtopic.php?t={topicId}",
            Title = title,
            InfoHashV1 = new string('a', 40),
            TorrentSizeBytes = 1_048_576,
            FileCount = 1,
            TorrentFilePath = $"/data/torrents/{topicId}.torrent",
            TorrentFileSha256 = new string('b', 64),
            TorrentFileDownloadedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };

    private static async Task<string?> ScalarAsync(DbConnection connection, string sql)
    {
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        object? result = await command.ExecuteScalarAsync();
        return Convert.ToString(result, CultureInfo.InvariantCulture);
    }
}
