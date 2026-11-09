using System.Globalization;
using System.Text;
using FootballApp.Application.Common.Models;
using FootballApp.Domain.Entities;
using FootballApp.Domain.Enums;

namespace FootballApp.Application.Clips;

/// <summary>Which tags may become a clip, and which stretch of the video each one turns into (FR-11, FR-13).</summary>
public static class ClipRules
{
    public const int MaxTagsPerClip = 50;
    public const int MaxLeadSeconds = 60;

    /// <summary>Keeps a single render short enough for a small worker container.</summary>
    public const int MaxClipDurationSeconds = 10 * 60;

    public static bool IsWithinVideo(int timestampSeconds, int videoDurationSeconds) =>
        timestampSeconds >= 0 && timestampSeconds <= videoDurationSeconds;

    /// <summary>[timestamp − lead-in, timestamp + lead-out], cut off at the start and end of the video.</summary>
    public static ClipSegment CalculateSegment(Tag tag, int videoDurationSeconds)
    {
        long start = (long)tag.TimestampSeconds - Math.Max(0, tag.LeadInSeconds);
        long end = (long)tag.TimestampSeconds + Math.Max(0, tag.LeadOutSeconds);

        return new ClipSegment(
            (int)Math.Clamp(start, 0, videoDurationSeconds),
            (int)Math.Clamp(end, 0, videoDurationSeconds));
    }

    /// <summary>One segment per tag, in the order given (ClipTag.Order).</summary>
    public static IReadOnlyList<ClipSegment> BuildSegments(IEnumerable<Tag> orderedTags, int videoDurationSeconds) =>
        orderedTags.Select(tag => CalculateSegment(tag, videoDurationSeconds)).ToList();

    /// <summary>Returns why these tags cannot be rendered, or null if they can. The message is shown to the user.</summary>
    public static string? Validate(ClipType type, IReadOnlyList<Tag> orderedTags, int videoDurationSeconds)
    {
        if (orderedTags.Count == 0)
        {
            return "At least one tag is required to generate a clip.";
        }

        if (type == ClipType.SingleClip && orderedTags.Count != 1)
        {
            return "A single clip is generated from exactly one tag.";
        }

        if (orderedTags.Count > MaxTagsPerClip)
        {
            return $"A highlight reel can include at most {MaxTagsPerClip} tags.";
        }

        long totalSeconds = 0;
        foreach (var tag in orderedTags)
        {
            if (!IsWithinVideo(tag.TimestampSeconds, videoDurationSeconds))
            {
                return $"The tag at {tag.TimestampSeconds}s is outside the video, which is {videoDurationSeconds}s long.";
            }

            var segment = CalculateSegment(tag, videoDurationSeconds);
            if (segment.DurationSeconds < 1)
            {
                return $"The tag at {tag.TimestampSeconds}s would produce an empty clip.";
            }

            totalSeconds += segment.DurationSeconds;
        }

        if (totalSeconds > MaxClipDurationSeconds)
        {
            return $"The selected tags add up to more than {MaxClipDurationSeconds / 60} minutes of video.";
        }

        return null;
    }

    /// <summary>
    /// Derived from the IDs only, so a retried render overwrites the same object instead of adding another.
    /// </summary>
    public static string BuildStorageKey(Guid matchId, Guid clipId) => $"matches/{matchId:N}/clips/{clipId:N}.mp4";

    /// <summary>Plain ASCII, safe to put in a Content-Disposition header.</summary>
    public static string BuildDownloadFileName(string matchTitle, ClipType type)
    {
        var slug = new StringBuilder();
        foreach (var c in matchTitle.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsAsciiLetterOrDigit(c))
            {
                slug.Append(char.ToLowerInvariant(c));
            }
            else if (slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }

            if (slug.Length >= 60)
            {
                break;
            }
        }

        var name = slug.ToString().Trim('-');
        var suffix = type == ClipType.HighlightReel ? "highlights" : "clip";
        return $"{(name.Length == 0 ? "match" : name)}-{suffix}.mp4";
    }
}
