using FootballApp.Application.Common.Interfaces;
using FootballApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FootballApp.Application.Tests.Fakes;

public class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options), IApplicationDbContext
{
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<Clip> Clips => Set<Clip>();
    public DbSet<ClipTag> ClipTags => Set<ClipTag>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<ClipTag>().HasKey(ct => new { ct.ClipId, ct.TagId });
    }

    public static TestDbContext Create() => new(
        new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
