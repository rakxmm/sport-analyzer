using FootballApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FootballApp.Infrastructure.Persistence.Configurations;

public class MatchConfiguration : IEntityTypeConfiguration<Match>
{
    public void Configure(EntityTypeBuilder<Match> builder)
    {
        builder.Property(m => m.OwnerId).IsRequired().HasMaxLength(64);
        builder.Property(m => m.Title).IsRequired().HasMaxLength(200);
        builder.HasIndex(m => m.OwnerId);

        builder.Property(m => m.VideoStorageKey).HasMaxLength(256);
        builder.Property(m => m.VideoFileName).HasMaxLength(255);
        builder.Property(m => m.VideoContentType).HasMaxLength(100);
        builder.Property(m => m.UploadId).HasMaxLength(1024);

        builder.HasMany(m => m.Tags)
            .WithOne(t => t.Match)
            .HasForeignKey(t => t.MatchId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(m => m.Clips)
            .WithOne(c => c.Match)
            .HasForeignKey(c => c.MatchId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
