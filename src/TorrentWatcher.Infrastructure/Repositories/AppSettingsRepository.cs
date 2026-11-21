using Microsoft.EntityFrameworkCore;
using TorrentWatcher.Application.Abstractions;
using TorrentWatcher.Domain.Entities;
using TorrentWatcher.Infrastructure.Persistence;

namespace TorrentWatcher.Infrastructure.Repositories;

public sealed class AppSettingsRepository : IAppSettingsRepository
{
    private readonly AppDbContext _db;

    public AppSettingsRepository(AppDbContext db)
    {
        _db = db;
    }

    // Без tracking: настройки могут измениться в другом scope (например, вход в рутрекер
    // сохраняет куку), и повторное чтение должно вернуть свежие значения, а не старый экземпляр.
    public Task<AppSetting?> FindAsync(CancellationToken cancellationToken) =>
        _db.AppSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(setting => setting.Id == AppSetting.DefaultId, cancellationToken);

    public async Task SaveAsync(AppSetting setting, CancellationToken cancellationToken)
    {
        // Настройки — одна строка: в одном scope их могли прочитать дважды (AsNoTracking),
        // и тогда предыдущий экземпляр уже отслеживается. Отсоединяем его, иначе EF Core
        // бросает «another instance with the same key value is already being tracked».
        AppSetting? tracked = _db.ChangeTracker
            .Entries<AppSetting>()
            .FirstOrDefault(entry =>
                !ReferenceEquals(entry.Entity, setting) && entry.Entity.Id == setting.Id)
            ?.Entity;

        if (tracked is not null)
        {
            _db.Entry(tracked).State = EntityState.Detached;
        }

        if (_db.Entry(setting).State == EntityState.Detached)
        {
            _db.AppSettings.Update(setting);
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
