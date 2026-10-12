using FootballApp.Application.Common.Interfaces;
using FootballApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FootballApp.Infrastructure.Persistence;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : DbContext(options), IApplicationDbContext
{
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<Clip> Clips => Set<Clip>();
    public DbSet<ClipTag> ClipTags => Set<ClipTag>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        base.OnModelCreating(builder);
    }
}
