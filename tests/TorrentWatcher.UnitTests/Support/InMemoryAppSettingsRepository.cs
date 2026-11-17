using TorrentWatcher.Application.Abstractions;
using TorrentWatcher.Domain.Entities;

namespace TorrentWatcher.UnitTests.Support;

/// <summary>Единственная строка настроек в памяти.</summary>
public sealed class InMemoryAppSettingsRepository(AppSetting? setting) : IAppSettingsRepository
{
    public AppSetting? Setting { get; private set; } = setting;

    public int SaveCount { get; private set; }

    public Task<AppSetting?> FindAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Setting);

    public Task SaveAsync(AppSetting settingToSave, CancellationToken cancellationToken)
    {
        Setting = settingToSave;
        SaveCount++;
        return Task.CompletedTask;
    }
}
