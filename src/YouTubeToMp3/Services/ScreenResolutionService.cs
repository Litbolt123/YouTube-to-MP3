using System.Runtime.InteropServices;
using System.Windows.Forms;
using Screen = System.Windows.Forms.Screen;

namespace YouTubeToMp3.Services;

public static class ScreenResolutionService
{
    private const int EnumCurrentSettings = -1;
    private const int MdEffectiveDpi = 0;
    private const uint MonitorDefaultToNearest = 2;

    private static int _cachedHeight;
    private static int _cachedWidth;

    public static void CaptureFromUiThread(IntPtr windowHandle = default)
    {
        var size = ReadPhysicalSize(ResolveScreen(windowHandle));
        if (size.Height > 0)
            _cachedHeight = size.Height;
        if (size.Width > 0)
            _cachedWidth = size.Width;
    }

    public static int PhysicalHeightPixels
    {
        get
        {
            if (_cachedHeight > 0)
                return _cachedHeight;
            var size = ReadPhysicalSize(ResolveScreen(IntPtr.Zero));
            _cachedHeight = size.Height > 0 ? size.Height : 1080;
            _cachedWidth = size.Width;
            return _cachedHeight;
        }
    }

    public static int PhysicalWidthPixels => _cachedWidth > 0 ? _cachedWidth : 1920;

    public static int PreferredVideoHeight => SnapToStandard(PhysicalHeightPixels);

    public static int ToPhysicalPixels(int logicalPixels, double dpiScale)
    {
        if (logicalPixels <= 0)
            return 0;
        if (dpiScale <= 0 || double.IsNaN(dpiScale) || double.IsInfinity(dpiScale))
            dpiScale = 1;
        return Math.Max(1, (int)Math.Round(logicalPixels * dpiScale, MidpointRounding.AwayFromZero));
    }

    public static int MapLogicalHeightToVideoHeight(int logicalHeight, double dpiScale) =>
        SnapToStandard(ToPhysicalPixels(logicalHeight, dpiScale));

    public static int SnapToStandard(int pixels)
    {
        if (pixels >= 2160) return 2160;
        if (pixels >= 1440) return 1440;
        if (pixels >= 1080) return 1080;
        if (pixels >= 720) return 720;
        if (pixels >= 480) return 480;
        return 360;
    }

    public static string DescribeScreenClass(int physicalHeight)
    {
        if (physicalHeight is >= 1560 and <= 1640)
            return "1600p-class";

        var snapped = SnapToStandard(physicalHeight);
        if (Math.Abs(physicalHeight - snapped) <= 8)
            return LabelForHeight(snapped);

        return physicalHeight + "p-class";
    }

    public static string MatchScreenMenuLabel()
    {
        var physical = PhysicalHeightPixels;
        var video = SnapToStandard(physical);
        var klass = DescribeScreenClass(physical);
        var rung = LabelForHeight(video);
        if (string.Equals(klass, rung, StringComparison.Ordinal))
            return "Match this screen (" + rung + ")";
        return "Match this screen (" + klass + ", up to " + rung + ")";
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

    private static Screen? ResolveScreen(IntPtr windowHandle)
    {
        try
        {
            if (windowHandle != IntPtr.Zero)
                return Screen.FromHandle(windowHandle);
            return Screen.FromPoint(Cursor.Position);
        }
        catch
        {
            try
            {
                return Screen.PrimaryScreen;
            }
            catch
            {
                return null;
            }
        }
    }

    private static (int Width, int Height) ReadPhysicalSize(Screen? screen)
    {
        if (screen is null)
            return (1920, 1080);

        // why: Screen.Bounds is logical pixels (2560x1600 at 150% reports 1707x1067). EnumDisplaySettings is the panel mode.
        if (TryReadDisplayMode(screen.DeviceName, out var width, out var height))
            return (width, height);

        var scale = TryDpiScale(screen);
        if (scale <= 1.05)
            return (Math.Max(1, screen.Bounds.Width), Math.Max(1, screen.Bounds.Height));

        return (
            ToPhysicalPixels(screen.Bounds.Width, scale),
            ToPhysicalPixels(screen.Bounds.Height, scale));
    }

    private static bool TryReadDisplayMode(string? deviceName, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (string.IsNullOrWhiteSpace(deviceName))
            return false;

        var mode = new DevMode();
        mode.dmSize = (short)Marshal.SizeOf<DevMode>();
        if (!EnumDisplaySettings(deviceName, EnumCurrentSettings, ref mode))
            return false;
        if (mode.dmPelsWidth <= 0 || mode.dmPelsHeight <= 0)
            return false;

        width = mode.dmPelsWidth;
        height = mode.dmPelsHeight;
        return true;
    }

    private static double TryDpiScale(Screen screen)
    {
        try
        {
            var point = new Point
            {
                X = screen.Bounds.Left + Math.Max(0, screen.Bounds.Width / 2),
                Y = screen.Bounds.Top + Math.Max(0, screen.Bounds.Height / 2),
            };
            var monitor = MonitorFromPoint(point, MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero)
                return 1;
            if (GetDpiForMonitor(monitor, MdEffectiveDpi, out var dpiX, out _) != 0 || dpiX == 0)
                return 1;
            return dpiX / 96d;
        }
        catch
        {
            return 1;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DevMode devMode);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(Point pt, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
    }
}
