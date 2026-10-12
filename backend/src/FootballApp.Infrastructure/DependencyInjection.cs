using Amazon.Runtime;
using Amazon.S3;
using FootballApp.Application.Common.Interfaces;
using FootballApp.Infrastructure.Auth;
using FootballApp.Infrastructure.Persistence;
using FootballApp.Infrastructure.Processing;
using FootballApp.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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

        // Validated on first use rather than at startup, so the rest of the API runs without R2 configured.
        services.AddOptions<R2Options>()
            .Bind(configuration.GetSection(R2Options.SectionName))
            .ValidateDataAnnotations();

        services.AddSingleton<IAmazonS3>(sp =>
        {
            var r2 = sp.GetRequiredService<IOptions<R2Options>>().Value;
            return new AmazonS3Client(
                new BasicAWSCredentials(r2.AccessKeyId, r2.SecretAccessKey),
                new AmazonS3Config
                {
                    ServiceURL = r2.ServiceUrl,
                    AuthenticationRegion = "auto",
                    ForcePathStyle = true,
                    // Only send checksums when an operation requires them — S3-compatible stores like R2 lag behind
                    // the SDK's newer default integrity headers.
                    RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                    ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
                });
        });
        services.AddScoped<IVideoStorageService, R2VideoStorageService>();
        services.AddScoped<IClipProcessingQueue, ClipProcessingQueue>();

        return services;
    }
}
