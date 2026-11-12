using System.Text;
using YouTubeToMp3.Services;
using Xunit;

namespace YouTubeToMp3.Tests;

public class YtDlpDownloadServiceTests
{
    [Fact]
    public void Duration_print_args_include_no_simulate_and_progress_is_kept()
    {
        var sb = new StringBuilder("--newline --progress ");
        YtDlpDownloadService.AppendDurationPrintArgs(sb);
        var args = sb.ToString();

        Assert.Contains("--progress", args, StringComparison.Ordinal);
        Assert.Contains("--print \"DURATION:%(duration)s\"", args, StringComparison.Ordinal);
        Assert.Contains("--no-simulate", args, StringComparison.Ordinal);
    }

    [Fact]
    public void Exit_zero_without_media_is_not_success()
    {
        var ok = YtDlpDownloadService.IsSuccessfulDownload(0, 0, out var partial, out var error);
        Assert.False(ok);
        Assert.False(partial);
        Assert.Equal("Download finished without producing a file. The video was not saved.", error);
    }

    [Fact]
    public void Exit_zero_with_media_is_success()
    {
        var ok = YtDlpDownloadService.IsSuccessfulDownload(0, 1, out var partial, out var error);
        Assert.True(ok);
        Assert.False(partial);
        Assert.Null(error);
    }

    [Fact]
    public void Nonzero_exit_with_media_is_partial_success()
    {
        var ok = YtDlpDownloadService.IsSuccessfulDownload(1, 2, out var partial, out var error);
        Assert.True(ok);
        Assert.True(partial);
        Assert.Null(error);
    }

    [Fact]
    public void Nonzero_exit_without_media_is_failure_without_empty_file_message()
    {
        var ok = YtDlpDownloadService.IsSuccessfulDownload(1, 0, out var partial, out var error);
        Assert.False(ok);
        Assert.False(partial);
        Assert.Null(error);
    }
}