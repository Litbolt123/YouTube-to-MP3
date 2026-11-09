using System.Windows.Forms;
using Screen = System.Windows.Forms.Screen;

namespace YouTubeToMp3.Services;

/// <summary>Primary display size for matching video download height to the screen.</summary>
public static class ScreenResolutionService
{
    private static int _cachedHeight;

    public static void CaptureFromUiThread()
    {
        _cachedHeight = ReadPrimaryHeight();
    }

    public static int PrimaryHeightPixels
    {
        get
        {
            if (_cachedHeight > 0)
                return _cachedHeight;
            _cachedHeight = ReadPrimaryHeight();
            return _cachedHeight;
        }
    }

    private static int ReadPrimaryHeight()
    {
        try
        {
            var screen = Screen.PrimaryScreen;
            if (screen is null)
                return 1080;

            return Math.Max(1, screen.Bounds.Height);
        }
        catch
        {
            return 1080;
        }
    }

    /// <summary>Standard YouTube rung at or just under the screen (1080p screen → 1080, 1440p → 1440).</summary>
    public static int PreferredVideoHeight => SnapToStandard(PrimaryHeightPixels);

    public static int SnapToStandard(int pixels)
    {
        if (pixels >= 2160) return 2160;
        if (pixels >= 1440) return 1440;
        if (pixels >= 1080) return 1080;
        if (pixels >= 720) return 720;
        if (pixels >= 480) return 480;
        return 360;
    }

    public static string LabelForHeight(int height) => height switch
    {
        >= 2160 => $"{height}p (4K)",
        1440 => "1440p",
        1080 => "1080p",
        720 => "720p",
        480 => "480p",
        360 => "360p",
        240 => "240p",
        144 => "144p",
        _ => $"{height}p",
    };
}
