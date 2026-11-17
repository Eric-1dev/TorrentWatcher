namespace TorrentWatcher.UnitTests.Support;

/// <summary>Фиксированное UTC-время для детерминированных проверок.</summary>
public sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public DateTimeOffset UtcNow { get; } = utcNow;

    public override DateTimeOffset GetUtcNow() => UtcNow;
}
