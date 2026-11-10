using YouTubeToMp3.Services;
using Xunit;

namespace YouTubeToMp3.Tests;

public class FfmpegProgressParserTests
{
    [Theory]
    [InlineData("frame=  306 fps=0.0 q=-1.0 size=175KiB time=00:00:05.10 bitrate=281.0kbits/s", 5.10)]
    [InlineData("out_time_ms=5100000", 5.10)]
    [InlineData("out_time=00:01:30.50", 90.50)]
    public void Parses_timestamps(string line, double expectedSeconds)
    {
        Assert.True(FfmpegProgressParser.TryParseTimestamp(line, out var value));
        Assert.InRange(value.TotalSeconds, expectedSeconds - 0.05, expectedSeconds + 0.05);
    }

    [Fact]
    public void Maps_time_to_percent()
    {
        var pct = FfmpegProgressParser.ToPercent(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2));
        Assert.NotNull(pct);
        Assert.InRange(pct!.Value, 24.9, 25.1);
    }

    [Fact]
    public void Parses_duration_marker()
    {
        Assert.True(FfmpegProgressParser.TryParseDuration("DURATION:3721", out var d));
        Assert.Equal(3721, d.TotalSeconds, 0);
    }
}

public class EtaSmootherTests
{
    [Fact]
    public void Smooths_jumps_instead_of_oscillating()
    {
        var smoother = new EtaSmoother(alpha: 0.2, maxJumpSeconds: 8);
        smoother.SetPhase(DownloadPhase.Downloading);
        Assert.Equal(TimeSpan.FromMinutes(2), smoother.Observe(TimeSpan.FromMinutes(2)));

        var next = smoother.Observe(TimeSpan.FromSeconds(20));
        Assert.NotNull(next);
        // why: raw jumped from 120s to 20s; clamp keeps it from collapsing in one tick
        Assert.InRange(next!.Value.TotalSeconds, 100, 120);
    }

    [Fact]
    public void CombinePhases_adds_merge_cushion()
    {
        var total = EtaSmoother.CombinePhases(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(3), null);
        Assert.Equal(TimeSpan.FromMinutes(4), total);
    }
}

public class ContentKindDetectorTests
{
    public static IEnumerable<object[]> LoggedExamples()
    {
        yield return ["https://youtu.be/vtKJgp_9Zbk?list=PLecUmotwpUZ4", "AI INCOME CHALLENGE | VIP SESSION #1", ContentKind.Video];
        yield return ["https://www.youtube.com/watch?v=QOtiU4pkd-E&list=PLecUmotwpUZ4&index=4", "AI INCOME CHALLENGE | VIP SESSION #2", ContentKind.Video];
        yield return ["https://www.youtube.com/live/WJ7FVoLgPN0", "Let's Start a $373/Day Side Hustle Together (LIVE)", ContentKind.Video];
        yield return ["https://www.youtube.com/watch?v=wfSFaxHuU7Q&list=PLecUmotwpUZ4&index=6", "Watch Me Build a $373/Day Side Hustle in Under 1 Hour (LIVE)", ContentKind.Video];
        yield return ["https://www.youtube.com/watch?v=ue0h67gEkxY", "Claude vs AI Agent Make Shaders From Scratch", ContentKind.Video];
        yield return ["https://www.youtube.com/watch?v=8kW9dZDqv8w", "Add Wii & GameCube Games to your Wii U Menu! (VC Injector Guide 2024)", ContentKind.Video];
        yield return ["https://music.youtube.com/watch?v=abc12345678", "Winter (The Wind Can Be Still)", ContentKind.Music];
        yield return ["https://www.youtube.com/watch?v=abcdEFGHijk&list=OLAK5uy_nCKeNwVgG9OLKFl0ffQkO3FM7sHifsoLA", "C418 - One - Surface Pension", ContentKind.Music];
        yield return ["https://www.youtube.com/watch?v=abcdEFGHijk", "C418 - Minecraft - Volume Alpha", ContentKind.Music];
        yield return ["https://www.youtube.com/watch?v=abcdEFGHijk", "Stardew Valley OST - Winter (The Wind Can Be Still)", ContentKind.Music];
        yield return ["https://www.youtube.com/watch?v=abcdEFGHijk", "Artist Name - Official Audio", ContentKind.Music];
        yield return ["https://www.youtube.com/watch?v=abcdEFGHijk", "Song Title (Official Lyric Video)", ContentKind.Music];
    }

    [Theory]
    [MemberData(nameof(LoggedExamples))]
    public void Detects_logged_examples(string url, string title, ContentKind expected)
    {
        ContentKindDetector.ClearPlaylistKindCache();
        var kind = ContentKindDetector.Detect(new ContentKindSignals
        {
            Url = url,
            Title = title,
            IsPlaylist = url.Contains("list=", StringComparison.OrdinalIgnoreCase),
        });
        Assert.Equal(expected, kind);
    }

    [Fact]
    public void Url_only_no_longer_treats_watch_plus_list_as_music()
    {
        ContentKindDetector.ClearPlaylistKindCache();
        var kind = ContentKindDetector.DetectFromUrl("https://youtu.be/vtKJgp_9Zbk?list=PLecUmotwpUZ4");
        Assert.Equal(ContentKind.Video, kind);
    }

    [Fact]
    public void Topic_channel_and_category_count_as_music()
    {
        ContentKindDetector.ClearPlaylistKindCache();
        var kind = ContentKindDetector.Detect(new ContentKindSignals
        {
            Url = "https://www.youtube.com/watch?v=abcdEFGHijk",
            Title = "Some Track",
            Channel = "C418 - Topic",
            CategoryId = 10,
        });
        Assert.Equal(ContentKind.Music, kind);
    }
}
