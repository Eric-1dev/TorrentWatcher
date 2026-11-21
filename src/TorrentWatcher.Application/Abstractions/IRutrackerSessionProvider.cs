using TorrentWatcher.Application.Dtos;

namespace TorrentWatcher.Application.Abstractions;

/// <summary>
/// Сеанс рутрекера для скачивания. Если куки в настройках нет, приложение само входит
/// по сохранённым логину и паролю и кэширует полученную куку. Браузер поднимается только
/// на время входа, а не на каждый запрос.
/// </summary>
public interface IRutrackerSessionProvider
{
    /// <summary>
    /// Возвращает куку сеанса, при необходимости выполняя вход. Бросает
    /// <see cref="Exceptions.RutrackerException"/>, если учётные данные не заданы или вход не удался.
    /// </summary>
    Task<string> GetCookieAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Принудительный вход по сохранённым данным для кнопки в настройках. Игнорирует
    /// паузу после предыдущей неудачи и не бросает исключение — результат возвращается как данные.
    /// </summary>
    Task<RutrackerLoginResult> LoginForUiAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Сбрасывает паузу после неудачного входа. Вызывается после смены логина или пароля,
    /// чтобы следующая попытка не ждала истечения окна.
    /// </summary>
    void Reset();
}
