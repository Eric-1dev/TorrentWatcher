namespace TorrentWatcher.Application.Dtos;

/// <summary>Результат проверки соединения с qBittorrent.</summary>
/// <param name="Success">Соединение и вход прошли.</param>
/// <param name="CheckedAtUtc">Время проверки, UTC.</param>
/// <param name="Message">Версия клиента или текст ошибки.</param>
public sealed record TorrentClientTestResult(
    bool Success,
    DateTime CheckedAtUtc,
    string Message);
