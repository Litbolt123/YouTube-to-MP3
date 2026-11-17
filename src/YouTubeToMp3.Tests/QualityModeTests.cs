using YouTubeToMp3.Services;
using Xunit;

namespace YouTubeToMp3.Tests;

public class VideoDownloadPlanTests
{
    private const string Compatible1080P60 =
        "bestvideo[height<=1080][fps<=60][vcodec^=avc1]+bestaudio[ext=m4a]/" +
        "bestvideo[height<=1080][fps<=60][vcodec^=avc1]+bestaudio[acodec^=mp4a]/" +
        "bestvideo[height<=1080][vcodec^=avc1]+bestaudio[ext=m4a]/" +
        "bestvideo[height<=1080][vcodec^=avc1]+bestaudio[acodec^=mp4a]/" +
        "bestvideo[height<=1080][vcodec^=h264]+bestaudio[ext=m4a]/" +
        "bestvideo[height<=1080][vcodec^=h264]+bestaudio[acodec^=mp4a]";

    private const string Best1080P60 =
        "bestvideo[height<=1080][fps<=60][vcodec^=vp9]+bestaudio/" +
        "bestvideo[height<=1080][fps<=60][vcodec^=vp09]+bestaudio/" +
        "bestvideo[height<=1080][vcodec^=vp9]+bestaudio/" +
        "bestvideo[height<=1080][fps<=60][vcodec^=av01]+bestaudio/" +
        "bestvideo[height<=1080][vcodec^=av01]+bestaudio/" +
        "bestvideo[height<=1080][fps<=60][vcodec!*=avc1]+bestaudio/" +
        "bestvideo[height<=1080]+bestaudio";

    [Fact]
    public void Compatible_1080p60_prefers_h264_m4a_in_mp4()
    {
        var plan = VideoDownloadPlan.Create(VideoQualityMode.Compatible, 5);
        Assert.Equal(Compatible1080P60, plan.FormatSelector);
        Assert.Equal("mp4", plan.Container);
        Assert.Contains("-c:v copy", plan.MergerArgs, StringComparison.Ordinal);
        Assert.Contains("-c:a copy", plan.MergerArgs, StringComparison.Ordinal);
        Assert.DoesNotContain("vp9", plan.FormatSelector, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("av01", plan.FormatSelector, StringComparison.OrdinalIgnoreCase);
        AssertEachBranchIsVideoPlusAudio(plan.FormatSelector);
    }

    [Fact]
    public void Best_1080p60_prefers_vp9_av1_in_mkv()
    {
        var plan = VideoDownloadPlan.Create(VideoQualityMode.Best, 5);
        Assert.Equal(Best1080P60, plan.FormatSelector);
        Assert.Equal("mkv", plan.Container);
        Assert.Contains("-c:v copy", plan.MergerArgs, StringComparison.Ordinal);
        Assert.Contains("-c:a copy", plan.MergerArgs, StringComparison.Ordinal);
        AssertEachBranchIsVideoPlusAudio(plan.FormatSelector);
    }

    [Fact]
    public void Opus_into_mp4_reencodes_audio_and_copies_video()
    {
        var args = VideoDownloadPlan.BuildMergerArgs("mp4", "opus");
        Assert.Contains("-c:v copy", args, StringComparison.Ordinal);
        Assert.Contains("-c:a aac", args, StringComparison.Ordinal);
        Assert.DoesNotContain("-c:a copy", args, StringComparison.Ordinal);
        Assert.False(VideoDownloadPlan.ShouldCopyAudio("mp4", "opus"));
        Assert.True(VideoDownloadPlan.ShouldCopyAudio("mkv", "opus"));
        Assert.True(VideoDownloadPlan.ShouldCopyAudio("mp4", "mp4a.40.2"));
    }

    [Theory]
    [InlineData(VideoQualityMode.Compatible, "mp4")]
    [InlineData(VideoQualityMode.Best, "mkv")]
    public void Container_follows_quality_mode(VideoQualityMode mode, string container)
    {
        var plan = VideoDownloadPlan.Create(mode, 2);
        Assert.Equal(container, plan.Container);
        Assert.Contains("[height<=1440]", plan.FormatSelector, StringComparison.Ordinal);
    }

    [Fact]
    public void Download_args_use_the_plan_and_log_the_selected_format()
    {
        var tools = new ToolCheckResult(true, "yt-dlp", true, "ffmpeg", false, null);
        var compatible = YtDlpDownloadService.BuildArguments(
            "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
            "out.%(ext)s",
            DownloadFormat.Mp4,
            DownloadScope.SingleVideo,
            ContentKind.Video,
            5,
            false,
            tools,
            null,
            1,
            false,
            VideoQualityMode.Compatible);

        Assert.Contains(Compatible1080P60, compatible, StringComparison.Ordinal);
        Assert.Contains("--merge-output-format mp4", compatible, StringComparison.Ordinal);
        Assert.Contains("--remux-video mp4", compatible, StringComparison.Ordinal);
        Assert.Contains("Merger+ffmpeg_o:-c:v copy -c:a copy", compatible, StringComparison.Ordinal);
        Assert.DoesNotContain("-c:a aac", compatible, StringComparison.Ordinal);
        Assert.DoesNotContain("vcodec^=vp9", compatible, StringComparison.Ordinal);
        Assert.Contains("SELECTED:%(format_id)s|%(vcodec)s|%(acodec)s|%(height)s|%(fps)s", compatible, StringComparison.Ordinal);

        var best = YtDlpDownloadService.BuildArguments(
            "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
            "out.%(ext)s",
            DownloadFormat.Mp4,
            DownloadScope.SingleVideo,
            ContentKind.Video,
            5,
            false,
            tools,
            null,
            1,
            false,
            VideoQualityMode.Best);

        Assert.Contains(Best1080P60, best, StringComparison.Ordinal);
        Assert.Contains("--merge-output-format mkv", best, StringComparison.Ordinal);
        Assert.Contains("--remux-video mkv", best, StringComparison.Ordinal);
        Assert.DoesNotContain("--merge-output-format mp4", best, StringComparison.Ordinal);
        Assert.DoesNotContain("--remux-video mp4", best, StringComparison.Ordinal);
    }

    private static void AssertEachBranchIsVideoPlusAudio(string selector)
    {
        foreach (var branch in selector.Split('/'))
        {
            Assert.StartsWith("bestvideo", branch, StringComparison.Ordinal);
            Assert.Contains("+bestaudio", branch, StringComparison.Ordinal);
            Assert.NotEqual("best", branch, StringComparer.Ordinal);
        }
    }
}

public class ScreenPixelMappingTests
{
    [Fact]
    public void Logical_1067_at_150_percent_is_1600p_class_not_720p()
    {
        var physical = ScreenResolutionService.ToPhysicalPixels(1067, 1.5);
        Assert.InRange(physical, 1560, 1640);
        Assert.Equal(1440, ScreenResolutionService.MapLogicalHeightToVideoHeight(1067, 1.5));
        Assert.Equal(720, ScreenResolutionService.SnapToStandard(1067));
        Assert.Equal("1600p-class", ScreenResolutionService.DescribeScreenClass(physical));
        Assert.Equal("1600p-class", ScreenResolutionService.DescribeScreenClass(1600));
        Assert.Equal(1440, ScreenResolutionService.SnapToStandard(1600));
    }

    [Theory]
    [InlineData(1080, 1.0, 1080)]
    [InlineData(1440, 1.0, 1440)]
    [InlineData(2160, 1.0, 2160)]
    [InlineData(720, 1.5, 1080)]
    [InlineData(1280, 1.25, 1440)]
    public void Dpi_scale_maps_to_youtube_rung(int logicalHeight, double scale, int videoHeight)
    {
        Assert.Equal(videoHeight, ScreenResolutionService.MapLogicalHeightToVideoHeight(logicalHeight, scale));
    }
}

public class MediaProbeFormatTests
{
    [Fact]
    public void Parses_selected_format_ids_and_codecs()
    {
        var line = "SELECTED:299+140|avc1.640028|mp4a.40.2|1080|60";
        Assert.True(DownloadDiagnostics.TryParseSelectedFormat(line, out var info));
        Assert.Equal("299+140", info.FormatId);
        Assert.Equal("avc1.640028", info.VideoCodec);
        Assert.Equal("mp4a.40.2", info.AudioCodec);
        var logged = DownloadDiagnostics.FormatSelectedLine(info);
        Assert.Contains("id=299+140", logged, StringComparison.Ordinal);
        Assert.Contains("vcodec=avc1.640028", logged, StringComparison.Ordinal);

        var summary = DownloadDiagnostics.BuildRunSummary(
            ["[info] abc: Downloading 1 format(s): 299+140", line],
            0,
            DownloadFormat.Mp4,
            new ToolCheckResult(true, "yt-dlp", true, "ffmpeg", false, null),
            true);
        var text = string.Join('\n', summary);
        Assert.Contains("selected_formats=299+140", text, StringComparison.Ordinal);
        Assert.Contains("selected_format id=299+140", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Ffprobe_summary_reports_real_picture_and_low_source_bitrate()
    {
        const string json = """
            {
              "streams": [
                {
                  "codec_type": "video",
                  "codec_name": "h264",
                  "width": 1920,
                  "height": 1080,
                  "avg_frame_rate": "60/1",
                  "bit_rate": "700000"
                },
                { "codec_type": "audio", "codec_name": "aac", "bit_rate": "128000" }
              ],
              "format": { "format_name": "mov,mp4,m4a", "bit_rate": "828000" }
            }
            """;

        var parsed = MediaProbeResult.ParseFfprobeJson(json, "clip.mp4", VideoQualityMode.Compatible, 1080);
        Assert.NotNull(parsed);
        Assert.Equal("1920x1080 60fps h264 mp4 700 kb/s", parsed!.Describe());
        Assert.Equal("Source is only ~700 kb/s - YouTube's copy is low quality", parsed.LowSourceNote);
        Assert.Contains("Source is only ~700 kb/s - YouTube's copy is low quality", parsed.Report(), StringComparison.Ordinal);
    }

    [Fact]
    public void Reports_fallback_when_saved_height_is_below_the_request()
    {
        var note = MediaProbeResult.DescribeFallback(VideoQualityMode.Compatible, 1080, 720, "h264");
        Assert.NotNull(note);
        Assert.Contains("720p", note, StringComparison.Ordinal);
        Assert.Contains("1080p", note, StringComparison.Ordinal);
        Assert.Null(MediaProbeResult.DescribeLowSourceBitrate(4_500_000));
    }
}

public class JobProgressTests
{
    [Theory]
    [InlineData(DownloadPhase.Downloading, 0, 0)]
    [InlineData(DownloadPhase.Downloading, 100, 75)]
    [InlineData(DownloadPhase.Merging, 0, 75)]
    [InlineData(DownloadPhase.Merging, 100, 95)]
    [InlineData(DownloadPhase.EmbeddingCover, 100, 99)]
    [InlineData(DownloadPhase.Finishing, 100, 100)]
    public void Overall_percent_covers_download_merge_and_finish(DownloadPhase phase, double phasePercent, double expected)
    {
        Assert.Equal(expected, JobProgress.ToOverall(phase, phasePercent));
    }
}
