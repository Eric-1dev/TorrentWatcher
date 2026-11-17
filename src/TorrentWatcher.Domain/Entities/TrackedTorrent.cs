namespace TorrentWatcher.Domain.Entities;

/// <summary>
/// Топик рутрекера, который приложение отслеживает. Хранится только последний скачанный
/// .torrent; «дата последнего обновления» — это <see cref="LastChangedAtUtc"/>.
/// </summary>
public sealed class TrackedTorrent
{
    public int Id { get; set; }

    /// <summary>Идентификатор топика из <c>viewtopic.php?t=</c>.</summary>
    public int TopicId { get; set; }

    /// <summary>Каноническая ссылка на топик.</summary>
    public string TopicUrl { get; set; } = string.Empty;

    /// <summary>Название из info-словаря .torrent.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Текущий известный infohash v1, 40 hex-символов.</summary>
    public string InfoHashV1 { get; set; } = string.Empty;

    public long TorrentSizeBytes { get; set; }

    public int FileCount { get; set; }

    /// <summary>Путь к последнему .torrent, например <c>/data/torrents/123.torrent</c>.</summary>
    public string TorrentFilePath { get; set; } = string.Empty;

    public string TorrentFileSha256 { get; set; } = string.Empty;

    public DateTime TorrentFileDownloadedAtUtc { get; set; }

    public DateTime? LastCheckedAtUtc { get; set; }

    public TorrentCheckOutcome LastCheckOutcome { get; set; }

    public string? LastError { get; set; }

    /// <summary>Дата последнего обнаруженного обновления.</summary>
    public DateTime? LastChangedAtUtc { get; set; }

    /// <summary>Пауза для отдельного торрента: выключенные не попадают в автоскан.</summary>
    public bool IsEnabled { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
