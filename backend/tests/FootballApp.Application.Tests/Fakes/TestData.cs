using FootballApp.Domain.Entities;
using FootballApp.Domain.Enums;

namespace FootballApp.Application.Tests.Fakes;

public static class TestData
{
    public const string OwnerId = "user_owner";
    public const string IntruderId = "user_intruder";

    /// <summary>A match whose video was uploaded and processed, ready for clips.</summary>
    public static Match ReadyMatch(int durationSeconds = 5400)
    {
        var match = new Match
        {
            OwnerId = OwnerId,
            Title = "Derby",
            MatchDate = new DateOnly(2026, 10, 1),
            ConsentConfirmed = true,
            ConsentConfirmedAt = DateTimeOffset.UtcNow,
            VideoStatus = VideoStatus.Ready,
            DurationSeconds = durationSeconds,
            VideoFileName = "derby.mp4",
            VideoContentType = "video/mp4",
            VideoSizeBytes = 1024
        };
        match.VideoStorageKey = $"matches/{match.Id:N}/source.mp4";
        return match;
    }

    /// <summary>The same match right after the upload finished, before the worker has looked at it.</summary>
    public static Match UploadedMatch()
    {
        var match = ReadyMatch();
        match.VideoStatus = VideoStatus.Uploaded;
        match.DurationSeconds = null;
        return match;
    }

    public static Tag Tag(Match match, int timestampSeconds, int leadInSeconds = 5, int leadOutSeconds = 3) => new()
    {
        MatchId = match.Id,
        Type = TagType.Goal,
        TimestampSeconds = timestampSeconds,
        LeadInSeconds = leadInSeconds,
        LeadOutSeconds = leadOutSeconds
    };

    public static Clip Clip(Match match, ClipType type, params Tag[] orderedTags)
    {
        var clip = new Clip { MatchId = match.Id, Type = type };
        clip.ClipTags = orderedTags
            .Select((tag, index) => new ClipTag { ClipId = clip.Id, TagId = tag.Id, Order = index })
            .ToList();
        return clip;
    }
}
