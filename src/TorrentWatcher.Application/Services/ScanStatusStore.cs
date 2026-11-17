using TorrentWatcher.Application.State;

namespace TorrentWatcher.Application.Services;

/// <summary>Потокобезопасный держатель последнего <see cref="ScanStatus"/> (singleton).</summary>
public sealed class ScanStatusStore
{
    private ScanStatus? _current;

    public ScanStatus? Current => Volatile.Read(ref _current);

    public void Set(ScanStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        Volatile.Write(ref _current, status);
    }
}
