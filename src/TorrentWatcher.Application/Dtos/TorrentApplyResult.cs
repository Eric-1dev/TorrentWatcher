namespace TorrentWatcher.Application.Dtos;

/// <summary>Результат применения обновления в торрент-клиенте.</summary>
/// <param name="Torrent">Актуальное состояние записи для UI.</param>
/// <param name="Applied">Обновление действительно применено (или уже было применено).</param>
/// <param name="ErrorCode">Машинный код ошибки; <c>null</c> — успех.</param>
/// <param name="Message">Пояснение для пользователя.</param>
public sealed record TorrentApplyResult(
    TorrentView Torrent,
    bool Applied,
    string? ErrorCode,
    string Message)
{
    public bool Success => ErrorCode is null;
}
