using FootballApp.Application;
using FootballApp.Infrastructure;
using FootballApp.Infrastructure.Processing;

// Background worker: takes video jobs off the Hangfire queue in Postgres and runs ffmpeg (FR-14).
// It serves no HTTP and is deployed as its own container, separately from the API (NFR-6).
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddProcessingServer();

var host = builder.Build();

FfmpegVideoProcessor.DeleteLeftoverScratchDirectories();

host.Run();
