using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace TorrentWatcher.Infrastructure.Persistence;

/// <summary>
/// Применяет PRAGMA на каждое открытое соединение SQLite: WAL для параллельного чтения,
/// busy_timeout на случай блокировки записи и включённые внешние ключи.
/// </summary>
public sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    public const int BusyTimeoutMs = 5000;

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) =>
        ApplyPragmas(connection);

    public override Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default) =>
        ApplyPragmasAsync(connection, cancellationToken);

    private static void ApplyPragmas(DbConnection connection)
    {
        using DbCommand command = connection.CreateCommand();
        foreach (string pragma in EnumeratePragmas())
        {
            command.CommandText = $"PRAGMA {pragma};";
            command.ExecuteNonQuery();
        }
    }

    private static async Task ApplyPragmasAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        using DbCommand command = connection.CreateCommand();
        foreach (string pragma in EnumeratePragmas())
        {
            command.CommandText = $"PRAGMA {pragma};";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static IEnumerable<string> EnumeratePragmas()
    {
        yield return "journal_mode=WAL";
        yield return $"busy_timeout={BusyTimeoutMs}";
        yield return "foreign_keys=ON";
    }
}
