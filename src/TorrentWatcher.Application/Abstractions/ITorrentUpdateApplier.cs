using TorrentWatcher.Application.Dtos;
using TorrentWatcher.Domain.Entities;

namespace TorrentWatcher.Application.Abstractions;

/// <summary>
/// Применение обнаруженного обновления в торрент-клиенте. Выделен в отдельный порт,
/// чтобы скан не зависел от qBittorrent и сбой применения не портил результат проверки.
/// </summary>
public interface ITorrentUpdateApplier
{
    /// <summary>
    /// Применяет обновление к раздаче. <paramref name="force"/> = <c>true</c> — ручная кнопка:
    /// игнорирует выключенное авто-применение и проверяет настройку с ошибкой, а не молча.
    /// </summary>
    Task<TorrentApplyResult> ApplyAsync(
        TrackedTorrent torrent,
        bool force,
        CancellationToken cancellationToken);

    /// <summary>Ручное применение по идентификатору записи.</summary>
    /// <exception cref="Exceptions.AppException">TORRENT_NOT_FOUND.</exception>
    Task<TorrentApplyResult> ApplyByIdAsync(int id, CancellationToken cancellationToken);
}
