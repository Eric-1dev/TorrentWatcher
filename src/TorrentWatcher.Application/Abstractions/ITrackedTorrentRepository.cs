using TorrentWatcher.Domain.Entities;

namespace TorrentWatcher.Application.Abstractions;

/// <summary>Хранилище отслеживаемых торрентов. Реализация — EF Core + SQLite в Infrastructure.</summary>
public interface ITrackedTorrentRepository
{
    Task<IReadOnlyList<TrackedTorrent>> GetAllAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<TrackedTorrent>> GetEnabledAsync(CancellationToken cancellationToken);

    Task<TrackedTorrent?> GetByIdAsync(int id, CancellationToken cancellationToken);

    Task<TrackedTorrent?> GetByTopicIdAsync(int topicId, CancellationToken cancellationToken);

    Task AddAsync(TrackedTorrent torrent, CancellationToken cancellationToken);

    Task UpdateAsync(TrackedTorrent torrent, CancellationToken cancellationToken);

    Task DeleteAsync(TrackedTorrent torrent, CancellationToken cancellationToken);
}
