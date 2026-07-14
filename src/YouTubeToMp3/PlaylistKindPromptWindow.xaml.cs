using System.Windows;
using YouTubeToMp3.Services;

namespace YouTubeToMp3;

public partial class PlaylistKindPromptWindow
{
    public ContentKind? SelectedKind { get; private set; }

    private PlaylistKindPromptWindow(string? playlistTitle)
    {
        DashboardUi.EnsureTheme(this);
        InitializeComponent();
        PlaylistTitleText.Text = string.IsNullOrWhiteSpace(playlistTitle)
            ? "This link points to a YouTube playlist."
            : playlistTitle;
    }

    public static ContentKind? Show(Window owner, string? playlistTitle = null)
    {
        var dlg = new PlaylistKindPromptWindow(playlistTitle) { Owner = owner };
        return dlg.ShowDialog() == true ? dlg.SelectedKind : null;
    }

    private void MusicAlbum_OnClick(object sender, RoutedEventArgs e)
    {
        SelectedKind = ContentKind.Music;
        DialogResult = true;
        Close();
    }

    private void VideoPlaylist_OnClick(object sender, RoutedEventArgs e)
    {
        SelectedKind = ContentKind.Video;
        DialogResult = true;
        Close();
    }
}
