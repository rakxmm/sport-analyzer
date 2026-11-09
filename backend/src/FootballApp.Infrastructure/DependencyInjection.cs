using Amazon.Runtime;
using Amazon.S3;
using FootballApp.Application.Clips;
using FootballApp.Application.Common.Interfaces;
using FootballApp.Infrastructure.Auth;
using FootballApp.Infrastructure.Persistence;
using FootballApp.Infrastructure.Processing;
using FootballApp.Infrastructure.Storage;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FootballApp.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Everything the API and the worker share: database, storage, and the client side of the job queue.</summary>
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

        services.AddOptions<ClipOptions>()
            .Bind(configuration.GetSection(ClipOptions.SectionName))
            .Validate(clips => clips.ShareLinkLifetime > TimeSpan.Zero, "Clips:ShareLinkLifetime must be a positive time span.");

        services.AddOptions<ProcessingOptions>()
            .Bind(configuration.GetSection(ProcessingOptions.SectionName))
            .ValidateDataAnnotations();

        // Registered for the API too: it never calls ffmpeg, but its container must be able to build the
        // worker-only command handlers that depend on this.
        services.AddSingleton<IVideoProcessor, FfmpegVideoProcessor>();

        // The queue lives in the same Postgres database (schema "hangfire", created on first use), so no
        // Redis or other paid service is needed. Nothing connects until the first job is enqueued.
        services.AddHangfire(hangfire => hangfire
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(
                postgres => postgres.UseNpgsqlConnection(configuration.GetConnectionString("Postgres")),
                new PostgreSqlStorageOptions
                {
                    QueuePollInterval = TimeSpan.FromSeconds(5),
                    // A job whose worker died is handed out again after this long. The sliding variant
                    // keeps renewing the claim while a job is still running, so a long render is never
                    // started a second time alongside itself.
                    InvisibilityTimeout = TimeSpan.FromMinutes(5),
                    UseSlidingInvisibilityTimeout = true
                }));

        services.AddScoped<HangfireProcessingQueue>();
        services.AddScoped<IClipProcessingQueue>(sp => sp.GetRequiredService<HangfireProcessingQueue>());
        services.AddScoped<IVideoProcessingQueue>(sp => sp.GetRequiredService<HangfireProcessingQueue>());

        return services;
    }

    /// <summary>Worker only: the Hangfire server that takes jobs off the queue and runs them.</summary>
    public static IServiceCollection AddProcessingServer(this IServiceCollection services)
    {
        services.AddHangfireServer((sp, server) =>
        {
            server.WorkerCount = sp.GetRequiredService<IOptions<ProcessingOptions>>().Value.WorkerCount;
        });

        return services;
    }
}
