using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TorrentWatcher.Domain.Entities;

namespace TorrentWatcher.Infrastructure.Persistence.Configurations;

public sealed class TrackedTorrentConfiguration : IEntityTypeConfiguration<TrackedTorrent>
{
    public void Configure(EntityTypeBuilder<TrackedTorrent> builder)
    {
        builder.ToTable("TrackedTorrents");

        builder.HasKey(torrent => torrent.Id);

        builder.Property(torrent => torrent.TopicId).IsRequired();
        builder.HasIndex(torrent => torrent.TopicId).IsUnique();

        builder.Property(torrent => torrent.TopicUrl).IsRequired().HasMaxLength(512);
        builder.Property(torrent => torrent.Title).IsRequired().HasMaxLength(512);
        builder.Property(torrent => torrent.InfoHashV1).IsRequired().HasMaxLength(40).IsFixedLength();
        builder.Property(torrent => torrent.TorrentFilePath).IsRequired().HasMaxLength(1024);
        builder.Property(torrent => torrent.TorrentFileSha256).IsRequired().HasMaxLength(64).IsFixedLength();
        builder.Property(torrent => torrent.LastCheckOutcome).IsRequired().HasConversion<int>();
        builder.Property(torrent => torrent.LastError).HasMaxLength(4000);
        builder.Property(torrent => torrent.AppliedInfoHashV1).HasMaxLength(40).IsFixedLength();
        builder.Property(torrent => torrent.SupersededInfoHashV1).HasMaxLength(40).IsFixedLength();
        builder.Property(torrent => torrent.ApplyError).HasMaxLength(4000);
        builder.Property(torrent => torrent.IsEnabled).IsRequired();
        builder.Property(torrent => torrent.CreatedAtUtc).IsRequired();
        builder.Property(torrent => torrent.UpdatedAtUtc).IsRequired();
    }
}
