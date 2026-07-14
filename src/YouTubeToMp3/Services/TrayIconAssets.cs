using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using System.Windows.Media.Imaging;
using Application = System.Windows.Application;
using DrawingFontStyle = System.Drawing.FontStyle;

namespace YouTubeToMp3.Services;

internal static class TrayIconAssets
{
    private static BitmapSource? _windowIcon;

    public static BitmapSource CreateWindowIcon()
    {
        if (_windowIcon is not null)
            return _windowIcon;

        try
        {
            var stream = Application.GetResourceStream(new Uri("pack://application:,,,/app.ico"))?.Stream;
            if (stream is not null)
            {
                using (stream)
                {
                    var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    frame.Freeze();
                    _windowIcon = frame;
                    return _windowIcon;
                }
            }
        }
        catch
        {
            /* fall through to drawn icon */
        }

        using var icon = CreateDrawnIcon();
        using var mem = new MemoryStream();
        icon.Save(mem);
        mem.Position = 0;
        var drawn = BitmapFrame.Create(mem, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        drawn.Freeze();
        _windowIcon = drawn;
        return _windowIcon;
    }

    public static Icon CreateIcon()
    {
        try
        {
            var stream = Application.GetResourceStream(new Uri("pack://application:,,,/app.ico"))?.Stream;
            if (stream is not null)
            {
                using (stream)
                {
                    using var icon = new Icon(stream);
                    return (Icon)icon.Clone();
                }
            }
        }
        catch
        {
            /* fall through */
        }

        return CreateDrawnIcon();
    }

    private static Icon CreateDrawnIcon()
    {
        using var bitmap = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        using (var fill = new SolidBrush(Color.FromArgb(211, 47, 47)))
            graphics.FillEllipse(fill, 1, 1, 30, 30);

        using var white = new Pen(Color.White, 2.6f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };

        graphics.DrawLine(white, 16, 9, 16, 21);
        graphics.DrawLine(white, 11.5f, 16, 16, 21);
        graphics.DrawLine(white, 20.5f, 16, 16, 21);

        var handle = bitmap.GetHicon();
        var icon = Icon.FromHandle(handle);
        var clone = (Icon)icon.Clone();
        DestroyIcon(handle);
        return clone;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr handle);

    public static void ApplyMenuStyle(ContextMenuStrip menu)
    {
        menu.Renderer = new DarkTrayMenuRenderer();
        menu.BackColor = TrayColors.Background;
        menu.ForeColor = TrayColors.Text;
        menu.Font = new Font("Segoe UI", 9.75f, DrawingFontStyle.Regular, GraphicsUnit.Point);
        menu.ShowImageMargin = false;
        menu.Padding = new Padding(6, 8, 6, 8);
    }

    public static ToolStripMenuItem CreateHeader(string text) =>
        new(text)
        {
            Enabled = false,
            ForeColor = TrayColors.Accent,
            Font = new Font("Segoe UI Semibold", 10f, DrawingFontStyle.Bold, GraphicsUnit.Point),
            Margin = new Padding(0, 0, 0, 4),
        };

    private static class TrayColors
    {
        public static readonly Color Background = Color.FromArgb(24, 28, 36);
        public static readonly Color Text = Color.FromArgb(242, 244, 248);
        public static readonly Color Muted = Color.FromArgb(154, 163, 178);
        public static readonly Color Accent = Color.FromArgb(239, 83, 80);
        public static readonly Color Hover = Color.FromArgb(48, 56, 70);
        public static readonly Color Border = Color.FromArgb(45, 52, 64);
    }

    private sealed class DarkTrayMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkTrayMenuRenderer() : base(new DarkTrayColorTable()) { }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (!e.Item.Enabled && e.Item is ToolStripMenuItem)
                e.TextColor = TrayColors.Accent;
            else if (!e.Item.Enabled)
                e.TextColor = TrayColors.Muted;
            else
                e.TextColor = TrayColors.Text;

            base.OnRenderItemText(e);
        }
    }

    private sealed class DarkTrayColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => TrayColors.Background;
        public override Color ImageMarginGradientBegin => TrayColors.Background;
        public override Color ImageMarginGradientMiddle => TrayColors.Background;
        public override Color ImageMarginGradientEnd => TrayColors.Background;
        public override Color MenuBorder => TrayColors.Border;
        public override Color MenuItemBorder => TrayColors.Border;
        public override Color MenuItemSelected => TrayColors.Hover;
        public override Color MenuItemSelectedGradientBegin => TrayColors.Hover;
        public override Color MenuItemSelectedGradientEnd => TrayColors.Hover;
        public override Color MenuItemPressedGradientBegin => TrayColors.Accent;
        public override Color MenuItemPressedGradientEnd => TrayColors.Accent;
        public override Color SeparatorDark => TrayColors.Border;
        public override Color SeparatorLight => TrayColors.Border;
    }
}
