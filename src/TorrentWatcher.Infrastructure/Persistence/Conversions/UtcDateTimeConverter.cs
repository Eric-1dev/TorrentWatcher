using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace TorrentWatcher.Infrastructure.Persistence.Conversions;

/// <summary>
/// SQLite не хранит DateTimeKind: перед записью приводим время к UTC,
/// после чтения помечаем его как UTC.
/// </summary>
public sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter()
        : base(
            value => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime(),
            value => DateTime.SpecifyKind(value, DateTimeKind.Utc))
    {
    }
}
