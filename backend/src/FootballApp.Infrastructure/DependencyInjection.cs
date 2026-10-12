using FootballApp.Application.Common.Interfaces;
using FootballApp.Infrastructure.Auth;
using FootballApp.Infrastructure.Persistence;
using FootballApp.Infrastructure.Processing;
using FootballApp.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FootballApp.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Postgres")));

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, CurrentUserService>();

        services.AddScoped<IVideoStorageService, R2VideoStorageService>();
        services.AddScoped<IClipProcessingQueue, ClipProcessingQueue>();

        return services;
    }
}
