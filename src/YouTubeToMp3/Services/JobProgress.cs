namespace YouTubeToMp3.Services;

public static class JobProgress
{
    public static double ToOverall(DownloadPhase phase, double phasePercent)
    {
        var p = Math.Clamp(phasePercent, 0, 100);
        // why: a raw download percent hits 100 before merge, then the bar jumped back to 0
        var overall = phase switch
        {
            DownloadPhase.Downloading => p * 0.75,
            DownloadPhase.Merging => 75 + (p * 0.20),
            DownloadPhase.EmbeddingCover => 95 + (p * 0.04),
            DownloadPhase.Finishing => 99 + (p * 0.01),
            _ => 0,
        };
        return Math.Round(Math.Clamp(overall, 0, 100), 1, MidpointRounding.AwayFromZero);
    }
}
