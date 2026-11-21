namespace TorrentWatcher.Application.Dtos;

/// <summary>
/// Результат входа в рутрекер по кнопке в настройках. Ошибка возвращается как данные,
/// чтобы UI показал её в тосте, а не как HTTP-сбой.
/// </summary>
public sealed record RutrackerLoginResult(
    bool Success,
    string Message,
    DateTime? LoggedInAtUtc);
