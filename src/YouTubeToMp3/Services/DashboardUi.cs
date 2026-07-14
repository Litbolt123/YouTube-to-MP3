using System.Linq;
using System.Windows;
using Application = System.Windows.Application;

namespace YouTubeToMp3.Services;

public static class DashboardUi
{
    public static readonly Uri LightThemeUri = new("Themes/DashboardTheme.xaml", UriKind.Relative);
    public static readonly Uri DarkThemeUri = new("Themes/DashboardThemeDark.xaml", UriKind.Relative);

    public static void ApplyAppTheme(bool dark)
    {
        var app = Application.Current;
        if (app is null)
            return;

        var target = dark ? DarkThemeUri : LightThemeUri;
        var merged = app.Resources.MergedDictionaries;

        for (var i = merged.Count - 1; i >= 0; i--)
        {
            if (merged[i].Source == LightThemeUri || merged[i].Source == DarkThemeUri)
                merged.RemoveAt(i);
        }

        merged.Insert(0, new ResourceDictionary { Source = target });
    }

    public static void EnsureTheme(Window window)
    {
        ApplyAppTheme(App.Settings.UseDarkTheme);
        window.Background = GetBrush("DashBgBrush");
        window.Icon = TrayIconAssets.CreateWindowIcon();
    }

    public static void EnsureTheme(FrameworkElement host) => ApplyAppTheme(App.Settings.UseDarkTheme);

    public static void ApplyTheme(Window window, bool dark)
    {
        ApplyAppTheme(dark);
        window.Background = GetBrush("DashBgBrush");
        window.Icon = TrayIconAssets.CreateWindowIcon();
    }

    public static void ApplyTheme(FrameworkElement host, bool dark) => ApplyAppTheme(dark);

    private static System.Windows.Media.Brush GetBrush(string key) =>
        Application.Current.TryFindResource(key) as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Transparent;
}
