using FootballApp.Application.Matches.Commands.AbortVideoUpload;
using FootballApp.Application.Matches.Commands.CompleteVideoUpload;
using FootballApp.Application.Matches.Commands.CreateMatch;
using FootballApp.Application.Matches.Commands.StartVideoUpload;
using FootballApp.Application.Matches.Queries.GetMatches;
using FootballApp.Application.Matches.Queries.GetMatchById;
using FootballApp.Application.Matches.Queries.GetMatchVideoUrl;
using FootballApp.Application.Matches.Queries.GetVideoPartUploadUrl;
using MediatR;

namespace FootballApp.Api.Endpoints;

public static class MatchesEndpoints
{
    public static void MapMatchesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/matches")
            .RequireAuthorization()
            .WithTags("Matches");

        group.MapGet("/", async (ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetMatchesQuery(), ct)));

        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetMatchByIdQuery(id), ct)));

        group.MapPost("/", async (CreateMatchRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CreateMatchCommand(request.Title, request.MatchDate, request.ConsentConfirmed), ct);
            return Results.Created($"/api/matches/{result.Id}", result);
        });

        group.MapGet("/{id:guid}/video", async (Guid id, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetMatchVideoUrlQuery(id), ct)));

        group.MapPost("/{id:guid}/upload", async (Guid id, StartVideoUploadRequest request, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(
                new StartVideoUploadCommand(id, request.FileName, request.ContentType, request.SizeBytes, request.LastModifiedMs), ct)));

        group.MapPost("/{id:guid}/upload/parts/{partNumber:int}", async (Guid id, int partNumber, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetVideoPartUploadUrlQuery(id, partNumber), ct)));

        group.MapPost("/{id:guid}/upload/complete", async (Guid id, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new CompleteVideoUploadCommand(id), ct)));

        group.MapDelete("/{id:guid}/upload", async (Guid id, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new AbortVideoUploadCommand(id), ct)));
    }

    public record CreateMatchRequest(string Title, DateOnly MatchDate, bool ConsentConfirmed);

    public record StartVideoUploadRequest(string FileName, string ContentType, long SizeBytes, long LastModifiedMs);
}
