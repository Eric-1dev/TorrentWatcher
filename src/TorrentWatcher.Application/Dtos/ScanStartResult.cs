using TorrentWatcher.Application.State;

namespace TorrentWatcher.Application.Dtos;

/// <summary>Ответ на запуск check-all: статус запуска и признак «стартовал новый».</summary>
public sealed record ScanStartResult(ScanStatus Status, bool Started)
{
    public Guid RunId => Status.RunId;
}
