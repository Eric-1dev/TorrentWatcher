namespace TorrentWatcher.Application.State;

/// <summary>Кто запустил скан.</summary>
public enum ScanTrigger
{
    /// <summary>Планировщик по интервалу.</summary>
    Auto = 0,

    /// <summary>Пользователь нажал «Проверить все».</summary>
    Manual = 1,
}

/// <summary>Состояние запуска скана.</summary>
public enum ScanRunState
{
    Running = 0,
    Completed = 1,
    Failed = 2,
}

/// <summary>
/// Снимок текущего/последнего скана. Хранится только в памяти: история сканов
/// в БД не ведётся, после рестарта статуса нет.
/// </summary>
/// <param name="RunId">Идентификатор запуска для UI и повторного check-all.</param>
/// <param name="Trigger">Авто- или ручной запуск.</param>
/// <param name="StartedAtUtc">Начало скана по UTC.</param>
/// <param name="FinishedAtUtc">Окончание; <c>null</c>, пока скан идёт.</param>
/// <param name="Status">Текущее состояние.</param>
/// <param name="TorrentsTotal">Сколько включённых торрентов в запуске.</param>
/// <param name="ChangedCount">Сколько обновилось.</param>
/// <param name="FailedCount">Сколько проверок завершилось ошибкой.</param>
/// <param name="Message">Причина прерывания или отмены, если была.</param>
public sealed record ScanStatus(
    Guid RunId,
    ScanTrigger Trigger,
    DateTime StartedAtUtc,
    DateTime? FinishedAtUtc,
    ScanRunState Status,
    int TorrentsTotal,
    int ChangedCount,
    int FailedCount,
    string? Message)
{
    public bool IsRunning => Status == ScanRunState.Running;
}
