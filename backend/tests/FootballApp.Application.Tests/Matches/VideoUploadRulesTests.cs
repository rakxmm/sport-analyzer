using FootballApp.Application.Matches.VideoUpload;

namespace FootballApp.Application.Tests.Matches;

public class VideoUploadRulesTests
{
    private const long MiB = 1024 * 1024;

    [Theory]
    [InlineData(1L, 32 * MiB)]
    [InlineData(15L * 1024 * MiB, 32 * MiB)]
    [InlineData(VideoUploadRules.MaxVideoSizeBytes, 32 * MiB)]
    [InlineData(500L * 1024 * MiB, 52 * MiB)]
    public void Part_size_stays_within_the_10000_part_limit(long sizeBytes, long expectedPartSize)
    {
        var partSize = VideoUploadRules.CalculatePartSize(sizeBytes);

        Assert.Equal(expectedPartSize, partSize);
        Assert.True(VideoUploadRules.CalculatePartCount(sizeBytes, partSize) <= 10_000);
    }

    [Theory]
    [InlineData("Derby.MP4", "source.mp4")]
    [InlineData("no-extension", "source")]
    [InlineData("weird.m p4", "source")]
    [InlineData("../../etc/passwd.mkv", "source.mkv")]
    public void Storage_key_only_keeps_a_safe_extension(string fileName, string expectedName)
    {
        var matchId = Guid.NewGuid();

        Assert.Equal($"matches/{matchId:N}/{expectedName}", VideoUploadRules.BuildStorageKey(matchId, fileName));
    }
}
