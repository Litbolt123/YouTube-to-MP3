using System.Globalization;
using System.Text;
using System.Windows.Controls;
using ComboBox = System.Windows.Controls.ComboBox;

namespace YouTubeToMp3.Services;

public static class QualityPresets
{
    public static IReadOnlyList<(string Label, int Value)> GetOptions(DownloadFormat format) =>
        DownloadFormats.IsVideo(format)
            ? new[]
            {
                ("Best available", 0),
                ("Up to 4K (2160p)", 1),
                ("Up to 1440p", 2),
                ("Up to 1080p", 5),
                ("Up to 720p (smaller)", 9),
                ("Up to 480p", 10),
                ("Up to 360p", 11),
                ("Up to 240p", 12),
                ("Up to 144p", 13),
            }
            : new[]
            {
                ("Best (VBR)", 0),
                ("Good (default)", 5),
                ("Smaller file", 9),
            };

    // Prefer highest-bitrate M4A/AAC when available; Opus decode via ffmpeg adds crackle on FLAC/MP3.
    private const string AacAudio =
        "bestaudio[ext=m4a][abr>=256]/bestaudio[ext=m4a][abr>=192]/bestaudio[ext=m4a]/bestaudio[acodec^=mp4a]/bestaudio";

    /// <summary>Prefer AAC/M4A for all transcodes (MP3/FLAC/WAV/M4A).</summary>
    public const string BestAudioFormatSelector = AacAudio;

    // YouTube serves 1080p+ as VP9/AV1 (webm), not native mp4. Height caps only; ffmpeg remuxes via --merge-output-format mp4.
    public static string GetMp4FormatSelector(int quality) => quality switch
    {
        1 => $"bestvideo[height<=2160]+{AacAudio}/best[height<=2160]/best",
        2 => $"bestvideo[height<=1440]+{AacAudio}/best[height<=1440]/best",
        5 => $"bestvideo[height<=1080]+{AacAudio}/best[height<=1080]/best",
        9 => $"bestvideo[height<=720]+{AacAudio}/best[height<=720]/best",
        10 => $"bestvideo[height<=480]+{AacAudio}/best[height<=480]/best",
        11 => $"bestvideo[height<=360]+{AacAudio}/best[height<=360]/best",
        12 => $"bestvideo[height<=240]+{AacAudio}/best[height<=240]/best",
        13 => $"bestvideo[height<=144]+{AacAudio}/best[height<=144]/best",
        _ => $"bestvideo+{AacAudio}/best",
    };

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

        return quality == 0 ? "Best" : quality.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>yt-dlp args for audio download/extract (format, extract, optional anti-clip filter).</summary>
    public static void AppendAudioExtractArgs(StringBuilder sb, DownloadFormat format, int audioQuality) =>
        AudioExtractArgs.Append(sb, format, audioQuality);
}
