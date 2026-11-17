namespace TorrentWatcher.Application.Dtos;

/// <summary>Список торрентов и сводка для шапки экрана.</summary>
/// <param name="Items">Торренты в порядке добавления.</param>
/// <param name="TotalCount">Всего отслеживаемых.</param>
/// <param name="UpdateAvailableCount">Сколько торрентов ждут обновления.</param>
public sealed record TorrentListResult(
    IReadOnlyList<TorrentView> Items,
    int TotalCount,
    int UpdateAvailableCount)
{
    public bool HasUpdates => UpdateAvailableCount > 0;
}
