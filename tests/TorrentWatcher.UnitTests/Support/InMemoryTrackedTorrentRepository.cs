using TorrentWatcher.Application.Abstractions;
using TorrentWatcher.Domain.Entities;

namespace TorrentWatcher.UnitTests.Support;

/// <summary>Репозиторий торрентов в памяти для юнит-тестов прикладного слоя.</summary>
public sealed class InMemoryTrackedTorrentRepository : ITrackedTorrentRepository
{
    private readonly List<TrackedTorrent> _torrents;
    private int _nextId;

    public InMemoryTrackedTorrentRepository(params TrackedTorrent[] torrents)
    {
        _torrents = [.. torrents];
        _nextId = _torrents.Count == 0 ? 1 : _torrents.Max(torrent => torrent.Id) + 1;
    }

    public IReadOnlyList<TrackedTorrent> Torrents => _torrents;

    public Task<IReadOnlyList<TrackedTorrent>> GetAllAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TrackedTorrent>>(
            _torrents.OrderBy(torrent => torrent.Id).ToArray());

    public Task<IReadOnlyList<TrackedTorrent>> GetEnabledAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TrackedTorrent>>(
            _torrents.Where(torrent => torrent.IsEnabled).OrderBy(torrent => torrent.Id).ToArray());

    public Task<TrackedTorrent?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        Task.FromResult(_torrents.FirstOrDefault(torrent => torrent.Id == id));

    public Task<TrackedTorrent?> GetByTopicIdAsync(int topicId, CancellationToken cancellationToken) =>
        Task.FromResult(_torrents.FirstOrDefault(torrent => torrent.TopicId == topicId));

    public Task AddAsync(TrackedTorrent torrent, CancellationToken cancellationToken)
    {
        if (torrent.Id == 0)
        {
            torrent.Id = _nextId++;
        }

        _torrents.Add(torrent);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(TrackedTorrent torrent, CancellationToken cancellationToken)
    {
        int index = _torrents.FindIndex(existing => existing.Id == torrent.Id);
        if (index < 0)
        {
            throw new InvalidOperationException($"Торрент {torrent.Id} не найден.");
        }

        _torrents[index] = torrent;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(TrackedTorrent torrent, CancellationToken cancellationToken)
    {
        _torrents.RemoveAll(existing => existing.Id == torrent.Id);
        return Task.CompletedTask;
    }
}
