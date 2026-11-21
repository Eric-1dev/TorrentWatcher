using TorrentWatcher.Domain.Entities;

namespace TorrentWatcher.Application.Dtos;

/// <summary>Торрент для таблицы UI: без пути к файлу на диске и без сырых байтов.</summary>
public sealed record TorrentView(
    int Id,
    int TopicId,
    string TopicUrl,
    string Title,
    string InfoHashV1,
    long TorrentSizeBytes,
    int FileCount,
    DateTime? LastChangedAtUtc,
    DateTime? LastCheckedAtUtc,
    TorrentCheckOutcome LastCheckOutcome,
    string? LastError,
    bool IsEnabled,
    DateTime TorrentFileDownloadedAtUtc,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    string? AppliedInfoHashV1,
    DateTime? AppliedAtUtc,
    string? ApplyError)
{
    public static TorrentView FromEntity(TrackedTorrent torrent)
    {
        ArgumentNullException.ThrowIfNull(torrent);

        return new TorrentView(
            torrent.Id,
            torrent.TopicId,
            torrent.TopicUrl,
            torrent.Title,
            torrent.InfoHashV1,
            torrent.TorrentSizeBytes,
            torrent.FileCount,
            torrent.LastChangedAtUtc,
            torrent.LastCheckedAtUtc,
            torrent.LastCheckOutcome,
            torrent.LastError,
            torrent.IsEnabled,
            torrent.TorrentFileDownloadedAtUtc,
            torrent.CreatedAtUtc,
            torrent.UpdatedAtUtc,
            torrent.AppliedInfoHashV1,
            torrent.AppliedAtUtc,
            torrent.ApplyError);
    }
}
