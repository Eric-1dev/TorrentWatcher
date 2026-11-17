using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TorrentWatcher.Domain.Entities;

namespace TorrentWatcher.Infrastructure.Persistence.Configurations;

public sealed class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    /// <summary>Фиксированная дата сида, чтобы миграция была детерминированной.</summary>
    private static readonly DateTime s_seedUpdatedAtUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public void Configure(EntityTypeBuilder<AppSetting> builder)
    {
        builder.ToTable("AppSettings");

        builder.HasKey(setting => setting.Id);
        builder.Property(setting => setting.Id).ValueGeneratedNever();

        builder.Property(setting => setting.RutrackerCookie).IsRequired().HasMaxLength(4000);
        builder.Property(setting => setting.CookieLastError).HasMaxLength(4000);
        builder.Property(setting => setting.ScanIntervalMinutes).IsRequired();
        builder.Property(setting => setting.AutoScanEnabled).IsRequired();
        builder.Property(setting => setting.RequestDelayMs).IsRequired();
        builder.Property(setting => setting.RequestTimeoutSeconds).IsRequired();
        builder.Property(setting => setting.UpdatedAtUtc).IsRequired();

        builder.HasData(new AppSetting
        {
            Id = AppSetting.DefaultId,
            RutrackerCookie = string.Empty,
            ScanIntervalMinutes = AppSetting.DefaultScanIntervalMinutes,
            AutoScanEnabled = true,
            RequestDelayMs = AppSetting.DefaultRequestDelayMs,
            RequestTimeoutSeconds = AppSetting.DefaultRequestTimeoutSeconds,
            UpdatedAtUtc = s_seedUpdatedAtUtc,
        });
    }
}
