namespace TorrentWatcher.Application.Dtos;

/// <summary>
/// Результат пробного скачивания. Ошибка проверки — не исключение: UI показывает
/// сообщение и остаётся на экране настроек.
/// </summary>
/// <param name="Success">Кука принята рутрекером.</param>
/// <param name="CheckedAtUtc">Момент проверки по UTC.</param>
/// <param name="Message">Текст для пользователя.</param>
public sealed record CookieTestResult(bool Success, DateTime CheckedAtUtc, string Message);
