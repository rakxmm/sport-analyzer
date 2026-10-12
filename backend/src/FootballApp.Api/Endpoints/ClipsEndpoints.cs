using FootballApp.Application.Clips.Commands.CreateClip;
using FootballApp.Application.Clips.Queries.GetClipsForMatch;
using FootballApp.Application.Clips.Queries.GetSharedClip;
using FootballApp.Domain.Enums;
using MediatR;

namespace FootballApp.Api.Endpoints;

public static class ClipsEndpoints
{
    public static void MapClipsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/matches/{matchId:guid}/clips")
            .RequireAuthorization()
            .WithTags("Clips");

        group.MapGet("/", async (Guid matchId, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetClipsForMatchQuery(matchId), ct)));

        group.MapPost("/", async (Guid matchId, CreateClipRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CreateClipCommand(matchId, request.Type, request.TagIds), ct);
            return Results.Created($"/api/matches/{matchId}/clips/{result.Id}", result);
        });

        // Public — viewable without logging in (FR-16). Deliberately outside the authorized group.
        app.MapGet("/api/shared-clips/{shareToken}", async (string shareToken, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetSharedClipQuery(shareToken), ct)))
            .WithTags("Clips")
            .AllowAnonymous();
    }

    public record CreateClipRequest(ClipType Type, IReadOnlyList<Guid> TagIds);
}
