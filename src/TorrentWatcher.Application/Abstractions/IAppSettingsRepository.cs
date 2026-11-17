using TorrentWatcher.Domain.Entities;

namespace TorrentWatcher.Application.Abstractions;

/// <summary>Хранилище единственной строки настроек (Id = 1).</summary>
public interface IAppSettingsRepository
{
    /// <summary>Возвращает строку настроек или <c>null</c>, если она почему-то отсутствует.</summary>
    Task<AppSetting?> FindAsync(CancellationToken cancellationToken);

    Task SaveAsync(AppSetting setting, CancellationToken cancellationToken);
}
