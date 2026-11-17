using TorrentWatcher.Application.Settings;

namespace TorrentWatcher.Application.Abstractions;

/// <summary>
/// Снапшот настроек в памяти (singleton). Заполняется при старте и обновляется
/// после сохранения настроек; <see cref="Changed"/> позволяет планировщику
/// пересчитать таймер без перезапуска приложения.
/// </summary>
public interface ISettingsProvider
{
    /// <summary>Текущие настройки. Чтение потокобезопасно.</summary>
    AppSettingsSnapshot Current { get; }

    /// <summary>Вызывается после каждого применения нового снапшота.</summary>
    event Action<AppSettingsSnapshot>? Changed;

    /// <summary>Публикует новый снапшот и оповещает подписчиков.</summary>
    void Apply(AppSettingsSnapshot snapshot);
}
