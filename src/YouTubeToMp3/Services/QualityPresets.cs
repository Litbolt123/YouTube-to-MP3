using System.Globalization;
using System.Text;
using System.Windows.Controls;
using ComboBox = System.Windows.Controls.ComboBox;

namespace YouTubeToMp3.Services;

public static class QualityPresets
{
    /// <summary>Cap MP4 height to this PC's display (see <see cref="ScreenResolutionService"/>).</summary>
    public const int MatchScreenQuality = -1;

    public static IReadOnlyList<(string Label, int Value)> GetOptions(DownloadFormat format)
    {
        if (!DownloadFormats.IsVideo(format))
        {
            return
            [
                ("Best (VBR)", 0),
                ("Good (default)", 5),
                ("Smaller file", 9),
            ];
        }

        var screen = ScreenResolutionService.PreferredVideoHeight;
        return
        [
            ($"Match this screen ({ScreenResolutionService.LabelForHeight(screen)})", -1),
            ("Best available (highest this video has)", 0),
            ("Up to 4K (2160p)", 1),
            ("Up to 1440p", 2),
            ("Up to 1080p", 5),
            ("Up to 720p", 9),
            ("Up to 480p", 10),
            ("Up to 360p", 11),
            ("Up to 240p", 12),
            ("Up to 144p", 13),
        ];
    }

    public static int DefaultVideoQuality(AppSettings settings)
    {
        if (settings.VideoQuality == MatchScreenQuality)
            return MatchScreenQuality;
        if (settings.MatchVideoToScreen && settings.VideoQuality == 0)
            return MatchScreenQuality;
        return settings.VideoQuality;
    }

    // Prefer highest-bitrate M4A/AAC when available; Opus decode via ffmpeg adds crackle on FLAC/MP3.
    private const string AacAudio =
        "bestaudio[ext=m4a][abr>=256]/bestaudio[ext=m4a][abr>=192]/bestaudio[ext=m4a]/bestaudio[acodec^=mp4a]/bestaudio";

    /// <summary>Prefer AAC/M4A for all transcodes (MP3/FLAC/WAV/M4A).</summary>
    public const string BestAudioFormatSelector = AacAudio;

    public static int? ResolveVideoHeightCap(int quality)
    {
        if (quality >= 144)
            return quality;

        if (quality == MatchScreenQuality)
            return ScreenResolutionService.PreferredVideoHeight;

        return quality switch
        {
            1 => 2160,
            2 => 1440,
            5 => 1080,
            9 => 720,
            10 => 480,
            11 => 360,
            12 => 240,
            13 => 144,
            _ => null,
        };
    }

    // YouTube 1080p+ is VP9/AV1. Each `/` must be a full video+audio pair —
    // splicing AacAudio (which contains `/`) turned later branches into audio-only .m4a.
    public static string GetMp4FormatSelector(int quality, bool matchScreen = true)
    {
        _ = matchScreen;
        var cap = ResolveVideoHeightCap(quality);
        var heightFilter = cap is int h ? $"[height<={h}]" : "";
        // Prefer VP9/H.264 before AV1. Format 400 (AV1 1440p) often hangs while "Testing format".
        return
            $"bestvideo{heightFilter}[vcodec!*=av01]+bestaudio[ext=m4a]/" +
            $"bestvideo{heightFilter}[vcodec!*=av01]+bestaudio/" +
            $"bestvideo{heightFilter}+bestaudio[ext=m4a]/" +
            $"bestvideo{heightFilter}+bestaudio";
    }

    public static void ApplyToCombo(ComboBox combo, DownloadFormat format, int selectedValue)
    {
        var previous = selectedValue;
        combo.Items.Clear();
        foreach (var (label, value) in GetOptions(format))
        {
            combo.Items.Add(new ComboBoxItem
            {
                Content = label,
                Tag = value.ToString(CultureInfo.InvariantCulture),
            });
        }

        SelectInCombo(combo, previous);
    }

    public static void SelectInCombo(ComboBox combo, int quality)
    {
        foreach (var item in combo.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag is string tag &&
                int.TryParse(tag, NumberStyles.Integer, CultureInfo.InvariantCulture, out var q) &&
                q == quality)
            {
                combo.SelectedItem = item;
                return;
            }
        }

        if (combo.Items.Count > 0)
            combo.SelectedIndex = 0;
    }

    public static int GetSelectedQuality(ComboBox combo)
    {
        if (combo.SelectedItem is ComboBoxItem item && item.Tag is string tag &&
            int.TryParse(tag, NumberStyles.Integer, CultureInfo.InvariantCulture, out var q))
            return q;
        return 0;
    }

    public static string GetLabel(DownloadFormat format, int quality)
    {
        foreach (var (label, value) in GetOptions(format))
        {
            if (value == quality)
                return label;
        }

        return quality switch
        {
            MatchScreenQuality => $"Match this screen ({ScreenResolutionService.LabelForHeight(ScreenResolutionService.PreferredVideoHeight)})",
            0 => DownloadFormats.IsVideo(format) ? "Best available" : "Best",
            _ when quality >= 144 => ScreenResolutionService.LabelForHeight(quality),
            _ => quality.ToString(CultureInfo.InvariantCulture),
        };
    }

    /// <summary>yt-dlp args for audio download/extract (format, extract, optional anti-clip filter).</summary>
    public static void AppendAudioExtractArgs(StringBuilder sb, DownloadFormat format, int audioQuality) =>
        AudioExtractArgs.Append(sb, format, audioQuality);
}
