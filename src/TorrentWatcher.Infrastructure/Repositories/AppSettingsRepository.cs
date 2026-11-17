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

    public Task<AppSetting?> FindAsync(CancellationToken cancellationToken) =>
        _db.AppSettings.FirstOrDefaultAsync(setting => setting.Id == AppSetting.DefaultId, cancellationToken);

    public async Task SaveAsync(AppSetting setting, CancellationToken cancellationToken)
    {
        if (_db.Entry(setting).State == EntityState.Detached)
        {
            _db.AppSettings.Update(setting);
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
