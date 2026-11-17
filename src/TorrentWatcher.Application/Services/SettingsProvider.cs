using TorrentWatcher.Application.Abstractions;
using TorrentWatcher.Application.Settings;

namespace TorrentWatcher.Application.Services;

/// <summary>
/// Потокобезопасный держатель снапшота настроек. Подписчики (планировщик) получают
/// событие после каждого сохранения, поэтому интервал и кука применяются без рестарта.
/// </summary>
public sealed class SettingsProvider : ISettingsProvider
{
    private AppSettingsSnapshot _current = AppSettingsSnapshot.Default;

    public AppSettingsSnapshot Current => Volatile.Read(ref _current);

    public event Action<AppSettingsSnapshot>? Changed;

    public void Apply(AppSettingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        Volatile.Write(ref _current, snapshot);
        Changed?.Invoke(snapshot);
    }
}
