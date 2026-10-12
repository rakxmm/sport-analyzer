using FootballApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FootballApp.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<Match> Matches { get; }
    DbSet<Tag> Tags { get; }
    DbSet<Clip> Clips { get; }
    DbSet<ClipTag> ClipTags { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
