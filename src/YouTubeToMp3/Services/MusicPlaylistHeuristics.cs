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
        "official lyric",
        "lyrics video",
        "lyric video",
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
        "music video",
        "theme song",
        "game audio",
        "nightcore",
        "lofi",
        "lo-fi",
        "remix",
        "instrumental",
    ];

    private static readonly string[] VideoKeywords =
    [
        "challenge",
        "vip session",
        "side hustle",
        "income",
        " live)",
        "(live",
        " livestream",
        "live stream",
        "tutorial",
        "how to",
        "guide",
        "walkthrough",
        "gameplay",
        "playthrough",
        "podcast",
        "interview",
        "reaction",
        "review",
        "vlog",
        "stream highlights",
        "speedrun",
        "let's play",
        "lets play",
        "unboxing",
        "news",
        "trailer",
        "devlog",
        "from scratch",
        "make shaders",
        "ai agent",
        "ai income",
    ];

    public static bool LooksLikeMusic(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return false;

        var normalized = $" {title.ToLowerInvariant()} ";
        if (LooksLikeVideo(title))
            return false;

        return MusicKeywords.Any(keyword => normalized.Contains(keyword, StringComparison.Ordinal));
    }

    public static bool LooksLikeVideo(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return false;

        var normalized = $" {title.ToLowerInvariant()} ";
        return VideoKeywords.Any(keyword => normalized.Contains(keyword, StringComparison.Ordinal));
    }
}
