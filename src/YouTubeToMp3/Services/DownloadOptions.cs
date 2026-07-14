using System.Windows.Controls;
using ComboBox = System.Windows.Controls.ComboBox;

namespace YouTubeToMp3.Services;

public enum DownloadFormat
{
    Mp3,
    M4a,
    Opus,
    Flac,
    Wav,
    Mp4,
}

public enum DownloadScope
{
    SingleVideo,
    Playlist,
}

public static class DownloadFormats
{
    private static readonly (string Label, DownloadFormat Format, string Tag)[] Options =
    [
        ("MP3 (audio)", DownloadFormat.Mp3, "mp3"),
        ("M4A (audio)", DownloadFormat.M4a, "m4a"),
        ("Opus (audio)", DownloadFormat.Opus, "opus"),
        ("FLAC (audio)", DownloadFormat.Flac, "flac"),
        ("WAV (audio)", DownloadFormat.Wav, "wav"),
        ("MP4 (video)", DownloadFormat.Mp4, "mp4"),
    ];

    public static bool IsAudio(DownloadFormat format) => format != DownloadFormat.Mp4;

    public static bool IsVideo(DownloadFormat format) => format == DownloadFormat.Mp4;

    public static string ToTag(DownloadFormat format) => format switch
    {
        DownloadFormat.M4a => "m4a",
        DownloadFormat.Opus => "opus",
        DownloadFormat.Flac => "flac",
        DownloadFormat.Wav => "wav",
        DownloadFormat.Mp4 => "mp4",
        _ => "mp3",
    };

    public static DownloadFormat FromTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return DownloadFormat.Mp3;

        foreach (var (_, format, optionTag) in Options)
        {
            if (string.Equals(optionTag, tag, StringComparison.OrdinalIgnoreCase))
                return format;
        }

        return DownloadFormat.Mp3;
    }

    public static string ToDisplayLabel(DownloadFormat format) => format switch
    {
        DownloadFormat.M4a => "M4A",
        DownloadFormat.Opus => "OPUS",
        DownloadFormat.Flac => "FLAC",
        DownloadFormat.Wav => "WAV",
        DownloadFormat.Mp4 => "MP4",
        _ => "MP3",
    };

    public static string ToYtDlpAudioFormat(DownloadFormat format) => ToTag(format);

    public static string FileExtension(DownloadFormat format) => $".{ToTag(format)}";

    public static void PopulateCombo(ComboBox combo, bool compactLabels = false)
    {
        combo.Items.Clear();
        foreach (var (label, format, tag) in Options)
        {
            combo.Items.Add(new ComboBoxItem
            {
                Content = compactLabels ? ToDisplayLabel(format) : label,
                Tag = tag,
            });
        }
    }

    public static DownloadFormat GetSelectedFormat(ComboBox combo)
    {
        if (combo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            return FromTag(tag);
        return DownloadFormat.Mp3;
    }

    public static void SelectInCombo(ComboBox combo, string? preferredTag)
    {
        var tag = ToTag(FromTag(preferredTag));
        foreach (var item in combo.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag is string itemTag &&
                string.Equals(itemTag, tag, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }

        if (combo.Items.Count > 0)
            combo.SelectedIndex = 0;
    }
}
