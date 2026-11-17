namespace TorrentWatcher.Domain.Entities;

/// <summary>Итог последней проверки торрента.</summary>
public enum TorrentCheckOutcome
{
    /// <summary>Торрент ещё ни разу не проверялся.</summary>
    Never = 0,

    /// <summary>Infohash не изменился.</summary>
    Unchanged = 1,

    /// <summary>Infohash изменился — обнаружено обновление.</summary>
    Changed = 2,

    /// <summary>Проверка завершилась ошибкой.</summary>
    Error = 3,
}
