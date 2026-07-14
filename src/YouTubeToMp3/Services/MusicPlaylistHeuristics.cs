namespace YouTubeToMp3.Services;

public static class MusicPlaylistHeuristics
{
    private static readonly string[] MusicKeywords =
    [
        " ost",
        "ost ",
        "soundtrack",
        "sound track",
        "full album",
        "official audio",
        "original score",
        "deluxe",
        " vol.",
        " vol ",
        "volume ",
        "volume alpha",
        "volume beta",
        " ep ",
        " lp ",
        "album",
        " songs",
        " tracks",
        "music",
        "theme",
        "game audio",
    ];

    public static bool LooksLikeMusic(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return false;

        var normalized = $" {title.ToLowerInvariant()} ";
        return MusicKeywords.Any(keyword => normalized.Contains(keyword, StringComparison.Ordinal));
    }
}
