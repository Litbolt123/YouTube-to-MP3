namespace YouTubeToMp3.Services;

/// <summary>Live job state for the YouTube page Download button.</summary>
public sealed class ExtensionJobStatus
{
    public string State { get; init; } = "idle";

    public bool FileExists { get; init; }

    public bool DownloadedBefore { get; init; }

    public double? Percent { get; init; }

    public string? Detail { get; init; }

    public string? Title { get; init; }

    public string? Path { get; init; }

    public string AfterDownload { get; init; } = "keepUntilFileGone";

    public int ResetSeconds { get; init; } = 5;
}
