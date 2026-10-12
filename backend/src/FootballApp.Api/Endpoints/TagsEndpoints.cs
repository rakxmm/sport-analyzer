using FootballApp.Application.Tags.Commands.CreateTag;
using FootballApp.Application.Tags.Commands.DeleteTag;
using FootballApp.Application.Tags.Queries.GetTagsForMatch;
using FootballApp.Domain.Enums;
using MediatR;

namespace FootballApp.Api.Endpoints;

public static class TagsEndpoints
{
    public static void MapTagsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/matches/{matchId:guid}/tags")
            .RequireAuthorization()
            .WithTags("Tags");

        group.MapGet("/", async (Guid matchId, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetTagsForMatchQuery(matchId), ct)));

        group.MapPost("/", async (Guid matchId, CreateTagRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CreateTagCommand(matchId, request.Type, request.TimestampSeconds, request.LeadInSeconds, request.LeadOutSeconds, request.Note),
                ct);
            return Results.Created($"/api/matches/{matchId}/tags/{result.Id}", result);
        });

        app.MapDelete("/api/tags/{tagId:guid}", async (Guid tagId, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteTagCommand(tagId), ct);
            return Results.NoContent();
        }).RequireAuthorization().WithTags("Tags");
    }

    public record CreateTagRequest(TagType Type, int TimestampSeconds, int LeadInSeconds, int LeadOutSeconds, string? Note);
}
