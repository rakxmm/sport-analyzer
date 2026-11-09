using FootballApp.Application.Clips;
using FootballApp.Application.Common.Models;
using FootballApp.Application.Tests.Fakes;
using FootballApp.Domain.Entities;
using FootballApp.Domain.Enums;

namespace FootballApp.Application.Tests.Clips;

public class ClipRulesTests
{
    private const int Duration = 5400;
    private readonly Match _match = TestData.ReadyMatch(Duration);

    [Theory]
    // timestamp, lead-in, lead-out, expected start, expected end
    [InlineData(100, 5, 3, 95, 103)]
    [InlineData(3, 5, 3, 0, 6)]
    [InlineData(5398, 5, 3, 5393, 5400)]
    [InlineData(0, 5, 3, 0, 3)]
    [InlineData(5400, 5, 3, 5395, 5400)]
    [InlineData(100, 0, 10, 100, 110)]
    [InlineData(100, 10, 0, 90, 100)]
    public void Segment_is_lead_in_to_lead_out_cut_off_at_the_ends_of_the_video(
        int timestamp, int leadIn, int leadOut, int expectedStart, int expectedEnd)
    {
        var segment = ClipRules.CalculateSegment(TestData.Tag(_match, timestamp, leadIn, leadOut), Duration);

        Assert.Equal(new ClipSegment(expectedStart, expectedEnd), segment);
        Assert.Equal(expectedEnd - expectedStart, segment.DurationSeconds);
    }

    [Fact]
    public void Negative_lead_values_count_as_zero()
    {
        var segment = ClipRules.CalculateSegment(TestData.Tag(_match, 100, leadInSeconds: -20, leadOutSeconds: 4), Duration);

        Assert.Equal(new ClipSegment(100, 104), segment);
    }

    [Fact]
    public void Huge_lead_values_do_not_overflow()
    {
        var segment = ClipRules.CalculateSegment(
            TestData.Tag(_match, int.MaxValue, leadInSeconds: int.MaxValue, leadOutSeconds: int.MaxValue), Duration);

        Assert.Equal(new ClipSegment(0, Duration), segment);
    }

    [Fact]
    public void Segments_keep_the_order_the_tags_were_given_in()
    {
        var tags = new[] { TestData.Tag(_match, 3000), TestData.Tag(_match, 60), TestData.Tag(_match, 1500) };

        var segments = ClipRules.BuildSegments(tags, Duration);

        Assert.Equal(
            [new ClipSegment(2995, 3003), new ClipSegment(55, 63), new ClipSegment(1495, 1503)],
            segments);
    }

    [Fact]
    public void Valid_selections_pass()
    {
        Assert.Null(ClipRules.Validate(ClipType.SingleClip, [TestData.Tag(_match, 100)], Duration));
        Assert.Null(ClipRules.Validate(ClipType.HighlightReel, [TestData.Tag(_match, 100), TestData.Tag(_match, 40)], Duration));
    }

    [Fact]
    public void A_clip_needs_at_least_one_tag()
    {
        Assert.NotNull(ClipRules.Validate(ClipType.HighlightReel, [], Duration));
    }

    [Fact]
    public void A_single_clip_takes_exactly_one_tag()
    {
        var tags = new[] { TestData.Tag(_match, 100), TestData.Tag(_match, 200) };

        Assert.NotNull(ClipRules.Validate(ClipType.SingleClip, tags, Duration));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(Duration + 1)]
    [InlineData(int.MaxValue)]
    public void Tags_outside_the_video_are_rejected(int timestamp)
    {
        var reason = ClipRules.Validate(ClipType.SingleClip, [TestData.Tag(_match, timestamp)], Duration);

        Assert.NotNull(reason);
        Assert.Contains("outside the video", reason);
    }

    [Fact]
    public void A_tag_at_the_very_end_of_the_video_is_still_inside_it()
    {
        Assert.Null(ClipRules.Validate(ClipType.SingleClip, [TestData.Tag(_match, Duration)], Duration));
    }

    [Fact]
    public void A_tag_that_would_cut_nothing_is_rejected()
    {
        var reason = ClipRules.Validate(
            ClipType.SingleClip, [TestData.Tag(_match, 100, leadInSeconds: 0, leadOutSeconds: 0)], Duration);

        Assert.NotNull(reason);
        Assert.Contains("empty", reason);
    }

    [Fact]
    public void Too_many_tags_are_rejected()
    {
        var tags = Enumerable.Range(0, ClipRules.MaxTagsPerClip + 1)
            .Select(i => TestData.Tag(_match, 100 + i * 20, leadInSeconds: 1, leadOutSeconds: 1))
            .ToList();

        Assert.NotNull(ClipRules.Validate(ClipType.HighlightReel, tags, Duration));
    }

    [Fact]
    public void A_reel_longer_than_the_limit_is_rejected()
    {
        // 6 tags x 120 s = 12 minutes
        var tags = Enumerable.Range(0, 6)
            .Select(i => TestData.Tag(_match, 500 + i * 300, leadInSeconds: 60, leadOutSeconds: 60))
            .ToList();

        var reason = ClipRules.Validate(ClipType.HighlightReel, tags, Duration);

        Assert.NotNull(reason);
        Assert.Contains("minutes", reason);
    }

    [Fact]
    public void Storage_key_depends_only_on_the_ids()
    {
        var matchId = Guid.NewGuid();
        var clipId = Guid.NewGuid();

        var key = ClipRules.BuildStorageKey(matchId, clipId);

        Assert.Equal($"matches/{matchId:N}/clips/{clipId:N}.mp4", key);
        Assert.Equal(key, ClipRules.BuildStorageKey(matchId, clipId));
    }

    [Theory]
    [InlineData("Derby", ClipType.SingleClip, "derby-clip.mp4")]
    [InlineData("Žilina – Trenčín (U15)", ClipType.HighlightReel, "zilina-trencin-u15-highlights.mp4")]
    [InlineData("  \"quotes\"\r\n; and/slashes\\ ", ClipType.SingleClip, "quotes-and-slashes-clip.mp4")]
    [InlineData("!!!", ClipType.SingleClip, "match-clip.mp4")]
    public void Download_file_name_is_plain_ascii(string title, ClipType type, string expected)
    {
        Assert.Equal(expected, ClipRules.BuildDownloadFileName(title, type));
    }

    [Fact]
    public void Download_file_name_stays_short()
    {
        var name = ClipRules.BuildDownloadFileName(new string('a', 500), ClipType.SingleClip);

        Assert.True(name.Length <= 70, name);
    }
}
