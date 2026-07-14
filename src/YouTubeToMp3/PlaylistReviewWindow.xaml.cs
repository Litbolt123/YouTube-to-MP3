using System.Collections.ObjectModel;
using System.Windows;
using YouTubeToMp3.Services;
using MessageBox = System.Windows.MessageBox;

namespace YouTubeToMp3;

public partial class PlaylistReviewWindow
{
    private readonly string _playlistUrl;
    private readonly DownloadFormat _format;
    private readonly ContentKind _contentKind;
    private readonly ObservableCollection<PlaylistTrackPlan> _tracks;

    public AlbumReviewResult Result { get; private set; }

    public PlaylistReviewWindow(AlbumReviewResult review, DownloadFormat format, ContentKind contentKind)
    {
        DashboardUi.EnsureTheme(this);
        InitializeComponent();

        _playlistUrl = review.PlaylistUrl;
        _format = format;
        _contentKind = contentKind;
        Result = review;
        _tracks = new ObservableCollection<PlaylistTrackPlan>(review.Tracks);

        CollectionTitleText.Text = review.CollectionTitle;
        AlbumArtistBox.Text = review.ArtistFolder;
        AlbumNameBox.Text = review.AlbumFolder;
        TracksGrid.ItemsSource = _tracks;
        UpdateStatusText();
    }

    private void TracksGrid_OnCellEditEnding(object sender, System.Windows.Controls.DataGridCellEditEndingEventArgs e)
    {
        UpdateStatusText();
    }

    private void ApplyToAll_OnClick(object sender, RoutedEventArgs e)
    {
        var albumArtist = AlbumArtistBox.Text.Trim();
        var album = AlbumNameBox.Text.Trim();
        foreach (var track in _tracks)
        {
            if (!string.IsNullOrWhiteSpace(album))
                track.Album = album;
        }

        if (!string.IsNullOrWhiteSpace(albumArtist) || !string.IsNullOrWhiteSpace(album))
        {
            CollectionTitleText.Text = MusicFilenameBuilder.FormatCollectionTitle(
                string.IsNullOrWhiteSpace(albumArtist) ? Result.ArtistFolder : albumArtist,
                string.IsNullOrWhiteSpace(album) ? Result.AlbumFolder : album);
        }

        UpdateStatusText();
    }

    private async void Redetect_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            StatusText.Text = "Re-detecting track details…";
            IsEnabled = false;

            var refreshed = await YtDlpMetadataService.GetAlbumReviewAsync(
                _playlistUrl,
                _format,
                _contentKind,
                new Progress<(int Done, int Total, string? Message)>(p =>
                {
                    Dispatcher.Invoke(() =>
                        StatusText.Text = p.Total > 0
                            ? $"Detecting track {p.Done} of {p.Total}…"
                            : p.Message ?? "Detecting…");
                })).ConfigureAwait(true);

            if (refreshed is null)
            {
                MessageBox.Show(this, "Could not re-detect album details.", "Review album", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Result = refreshed;
            CollectionTitleText.Text = refreshed.CollectionTitle;
            AlbumArtistBox.Text = refreshed.ArtistFolder;
            AlbumNameBox.Text = refreshed.AlbumFolder;
            _tracks.Clear();
            foreach (var track in refreshed.Tracks)
                _tracks.Add(track);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Review album", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsEnabled = true;
            UpdateStatusText();
        }
    }

    private void Download_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_tracks.Any(static t => t.Include))
        {
            MessageBox.Show(this, "Select at least one track to download.", "Review album", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var artistFolder = AlbumArtistBox.Text.Trim();
        var albumFolder = AlbumNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(artistFolder))
            artistFolder = Result.ArtistFolder;
        if (string.IsNullOrWhiteSpace(albumFolder))
            albumFolder = Result.AlbumFolder;

        foreach (var track in _tracks.Where(static t => t.Include))
        {
            if (!string.IsNullOrWhiteSpace(albumFolder))
                track.Album = albumFolder;
        }

        var collectionTitle = MusicFilenameBuilder.FormatCollectionTitle(artistFolder, albumFolder);
        CollectionTitleText.Text = collectionTitle;

        Result = new AlbumReviewResult
        {
            PlaylistUrl = _playlistUrl,
            CollectionTitle = collectionTitle,
            ArtistFolder = artistFolder,
            AlbumFolder = albumFolder,
            Tracks = _tracks.ToList(),
        };

        DialogResult = true;
        Close();
    }

    private void UpdateStatusText()
    {
        var included = _tracks.Count(static t => t.Include);
        var edited = _tracks.Count(static t => t.Include && t.HasFieldEdits());
        StatusText.Text = included == _tracks.Count
            ? $"{included} tracks · {edited} with manual edits"
            : $"{included} of {_tracks.Count} tracks selected · {edited} with manual edits";
    }
}
