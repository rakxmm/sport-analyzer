using FootballApp.Api.Endpoints;
using FootballApp.Api.Middleware;
using FootballApp.Application;
using FootballApp.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

// The frontend exchanges enums by name ("PendingUpload", "Goal"), not by number.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Clerk issues the JWT; the API only validates it against Clerk's JWKS — no custom auth code (FR-3).
var clerkAuthority = builder.Configuration["Clerk:Authority"];
if (string.IsNullOrWhiteSpace(clerkAuthority))
{
    throw new InvalidOperationException("Clerk:Authority is not configured (see appsettings).");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = clerkAuthority;
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = clerkAuthority,
            // Clerk session tokens carry no "aud" claim by default; set Clerk:Audience and flip this
            // on if you configure a custom JWT template with an audience.
            ValidateAudience = false
        };
    });
builder.Services.AddAuthorization();

var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(corsOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod());
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseHttpsRedirection();
app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

app.MapMatchesEndpoints();
app.MapTagsEndpoints();
app.MapClipsEndpoints();

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

app.Run();
