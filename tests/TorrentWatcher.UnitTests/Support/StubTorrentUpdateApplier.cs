using TorrentWatcher.Application.Abstractions;
using TorrentWatcher.Application.Dtos;
using TorrentWatcher.Domain.Entities;

namespace TorrentWatcher.UnitTests.Support;

/// <summary>Применение обновлений-заглушка: скан не должен зависеть от qBittorrent.</summary>
public sealed class StubTorrentUpdateApplier : ITorrentUpdateApplier
{
    public int ApplyCount { get; private set; }

    public List<bool> ForceFlags { get; } = [];

    public Task<TorrentApplyResult> ApplyAsync(
        TrackedTorrent torrent,
        bool force,
        CancellationToken cancellationToken)
    {
        ApplyCount++;
        ForceFlags.Add(force);

        return Task.FromResult(new TorrentApplyResult(
            TorrentView.FromEntity(torrent),
            Applied: false,
            ErrorCode: null,
            "заглушка"));
    }

    public Task<TorrentApplyResult> ApplyByIdAsync(int id, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
