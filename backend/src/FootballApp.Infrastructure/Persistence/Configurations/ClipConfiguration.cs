using FootballApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FootballApp.Infrastructure.Persistence.Configurations;

public class ClipConfiguration : IEntityTypeConfiguration<Clip>
{
    public void Configure(EntityTypeBuilder<Clip> builder)
    {
        builder.Property(c => c.ShareToken).IsRequired().HasMaxLength(64);
        builder.HasIndex(c => c.ShareToken).IsUnique();
    }
}

public class ClipTagConfiguration : IEntityTypeConfiguration<ClipTag>
{
    public void Configure(EntityTypeBuilder<ClipTag> builder)
    {
        builder.HasKey(ct => new { ct.ClipId, ct.TagId });

        builder.HasOne(ct => ct.Clip)
            .WithMany(c => c.ClipTags)
            .HasForeignKey(ct => ct.ClipId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(ct => ct.Tag)
            .WithMany()
            .HasForeignKey(ct => ct.TagId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
