using FootballApp.Application.Common.Exceptions;
using FootballApp.Application.Common.Interfaces;
using FootballApp.Domain.Entities;
using FootballApp.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FootballApp.Application.Clips.Commands.RenderClip;

/// <summary>
/// Worker job: cuts the clip's tags out of the match video, stores the MP4 and opens the share link
/// (FR-13 to FR-15). Not exposed through the API, so there is no owner check. Safe to run more than once
/// for the same clip: a finished clip is left alone and a retry overwrites the same storage object.
/// </summary>
public record RenderClipCommand(Guid ClipId) : IRequest;

public class RenderClipCommandHandler(
    IApplicationDbContext db,
    IVideoStorageService storage,
    IVideoProcessor processor,
    IOptions<ClipOptions> options) : IRequestHandler<RenderClipCommand>
{
    // ffmpeg keeps reading the source for the whole render, so the URL has to outlive the longest one.
    private static readonly TimeSpan SourceUrlLifetime = TimeSpan.FromHours(6);

    public async Task Handle(RenderClipCommand request, CancellationToken cancellationToken)
    {
        // The API queues this job right before it saves the clip, so the job can get here first.
        // Throwing makes the queue try again a little later.
        var clip = await db.Clips
            .Include(c => c.Match)
            .Include(c => c.ClipTags)
            .ThenInclude(ct => ct.Tag)
            .FirstOrDefaultAsync(c => c.Id == request.ClipId, cancellationToken)
            ?? throw new NotFoundException(nameof(Clip), request.ClipId);

        if (!ClipLifecycle.IsUnfinished(clip))
        {
            return;
        }

        var match = clip.Match!;
        if (match.VideoStatus != VideoStatus.Ready || match.VideoStorageKey is null || match.DurationSeconds is null)
        {
            await Fail(clip, "The match video is not available.", cancellationToken);
            return;
        }

        var tags = clip.ClipTags.OrderBy(ct => ct.Order).Select(ct => ct.Tag!).ToList();
        if (ClipRules.Validate(clip.Type, tags, match.DurationSeconds.Value) is { } invalidReason)
        {
            await Fail(clip, invalidReason, cancellationToken);
            return;
        }

        ClipLifecycle.MarkProcessing(clip);
        await db.SaveChangesAsync(cancellationToken);

        var segments = ClipRules.BuildSegments(tags, match.DurationSeconds.Value);
        var storageKey = ClipRules.BuildStorageKey(match.Id, clip.Id);

        try
        {
            var sourceUrl = await storage.GetSignedDownloadUrlAsync(match.VideoStorageKey, SourceUrlLifetime, cancellationToken);
            using var rendered = await processor.RenderAsync(sourceUrl, segments, cancellationToken);
            await storage.UploadFileAsync(storageKey, rendered.FilePath, "video/mp4", cancellationToken);
        }
        catch (InvalidVideoException ex)
        {
            await Fail(clip, ex.Message, cancellationToken);
            return;
        }

        ClipLifecycle.MarkDone(clip, storageKey, DateTimeOffset.UtcNow, options.Value.ShareLinkLifetime);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task Fail(Clip clip, string reason, CancellationToken cancellationToken)
    {
        ClipLifecycle.MarkFailed(clip, reason);
        await db.SaveChangesAsync(cancellationToken);
    }
}
