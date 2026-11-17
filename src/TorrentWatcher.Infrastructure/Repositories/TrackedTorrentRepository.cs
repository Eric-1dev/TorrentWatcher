using Microsoft.EntityFrameworkCore;
using TorrentWatcher.Application.Abstractions;
using TorrentWatcher.Domain.Entities;
using TorrentWatcher.Infrastructure.Persistence;

namespace TorrentWatcher.Infrastructure.Repositories;

public sealed class TrackedTorrentRepository : ITrackedTorrentRepository
{
    private readonly AppDbContext _db;

    public TrackedTorrentRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<TrackedTorrent>> GetAllAsync(CancellationToken cancellationToken) =>
        await _db.TrackedTorrents
            .AsNoTracking()
            .OrderBy(torrent => torrent.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TrackedTorrent>> GetEnabledAsync(CancellationToken cancellationToken) =>
        await _db.TrackedTorrents
            .AsNoTracking()
            .Where(torrent => torrent.IsEnabled)
            .OrderBy(torrent => torrent.Id)
            .ToListAsync(cancellationToken);

    public Task<TrackedTorrent?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        _db.TrackedTorrents.FirstOrDefaultAsync(torrent => torrent.Id == id, cancellationToken);

    public Task<TrackedTorrent?> GetByTopicIdAsync(int topicId, CancellationToken cancellationToken) =>
        _db.TrackedTorrents.FirstOrDefaultAsync(torrent => torrent.TopicId == topicId, cancellationToken);

    public async Task AddAsync(TrackedTorrent torrent, CancellationToken cancellationToken)
    {
        _db.TrackedTorrents.Add(torrent);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(TrackedTorrent torrent, CancellationToken cancellationToken)
    {
        _db.TrackedTorrents.Update(torrent);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(TrackedTorrent torrent, CancellationToken cancellationToken)
    {
        _db.TrackedTorrents.Remove(torrent);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
