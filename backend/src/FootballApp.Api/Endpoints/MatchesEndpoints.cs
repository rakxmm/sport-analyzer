using FootballApp.Application.Matches.Commands.CreateMatch;
using FootballApp.Application.Matches.Queries.GetMatches;
using FootballApp.Application.Matches.Queries.GetMatchById;
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
    }

    public record CreateMatchRequest(string Title, DateOnly MatchDate, bool ConsentConfirmed);
}
