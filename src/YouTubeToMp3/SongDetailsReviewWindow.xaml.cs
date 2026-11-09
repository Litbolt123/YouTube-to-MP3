using System.Windows;
using System.Windows.Controls;
using YouTubeToMp3.Services;
using MessageBox = System.Windows.MessageBox;

namespace YouTubeToMp3;

public partial class SongDetailsReviewWindow
{
    private readonly string _url;
    private readonly DownloadFormat _format;
    private readonly ContentKind _contentKind;
    private SongDetailsResult _initial;
    private bool _fileNameManuallyEdited;
    private bool _suppressFileNameSync;

    public SongDetailsResult Result { get; private set; }

    /// <summary>True when the user chose Apply (save without downloading).</summary>
    public bool ApplyOnly { get; private set; }

    public SongDetailsReviewWindow(SongDetailsResult details, DownloadFormat format, ContentKind contentKind)
    {
        DashboardUi.EnsureTheme(this);
        InitializeComponent();

        _url = details.Url;
        _format = format;
        _contentKind = contentKind;
        _initial = details;
        Result = details;
        LoadFields(details);
        DetectedHintText.Text = BuildDetectedHint(details);
        UpdateStatusText();
        TitleBox.Focus();
        TitleBox.SelectAll();
    }

    private void LoadFields(SongDetailsResult details)
    {
        _suppressFileNameSync = true;
        TitleBox.Text = details.Title;
        ArtistBox.Text = details.Artist;
        AlbumArtistBox.Text = details.AlbumArtist;
        AlbumBox.Text = details.Album;
        FileNameBox.Text = details.FileName;
        _suppressFileNameSync = false;
        _fileNameManuallyEdited = !string.Equals(
            details.FileName,
            SongDetailsResult.BuildDefaultFileName(details.Artist, details.Album, details.Title),
            StringComparison.Ordinal);
    }

    private void Field_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressFileNameSync || _fileNameManuallyEdited)
        {
            UpdateStatusText();
            return;
        }

        _suppressFileNameSync = true;
        FileNameBox.Text = SongDetailsResult.BuildDefaultFileName(
            ArtistBox.Text, AlbumBox.Text, TitleBox.Text);
        _suppressFileNameSync = false;
        UpdateStatusText();
    }

    private void FileNameBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressFileNameSync)
            return;

        var autoName = SongDetailsResult.BuildDefaultFileName(
            ArtistBox.Text, AlbumBox.Text, TitleBox.Text);
        _fileNameManuallyEdited = !string.Equals(
            FileNameBox.Text.Trim(),
            autoName,
            StringComparison.Ordinal);
        UpdateStatusText();
    }

    private void Reset_OnClick(object sender, RoutedEventArgs e)
    {
        LoadFields(_initial);
        UpdateStatusText();
    }

    private async void Redetect_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            StatusText.Text = "Re-detecting song details…";
            IsEnabled = false;

            var metadata = await YtDlpMetadataService.GetTrackMetadataAsync(
                _url, DownloadScope.SingleVideo, _format, _contentKind).ConfigureAwait(true);

            if (metadata is null)
            {
                MessageBox.Show(this, "Could not re-detect song details.", "Edit song details", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var refreshed = SongDetailsResult.FromMetadata(_url, metadata);
            _initial = refreshed;
            LoadFields(refreshed);
            DetectedHintText.Text = BuildDetectedHint(refreshed);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Edit song details", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsEnabled = true;
            UpdateStatusText();
        }
    }

    private void Apply_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryCommitResult(applyOnly: true))
            return;

        ApplyOnly = true;
        DialogResult = true;
        Close();
    }

    private void Download_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryCommitResult(applyOnly: false))
            return;

        ApplyOnly = false;
        DialogResult = true;
        Close();
    }

    private bool TryCommitResult(bool applyOnly)
    {
        var title = TitleBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            MessageBox.Show(this, "Title is required.", "Edit song details", MessageBoxButton.OK, MessageBoxImage.Information);
            TitleBox.Focus();
            return false;
        }

        var artist = ArtistBox.Text.Trim();
        var albumArtist = AlbumArtistBox.Text.Trim();
        var album = AlbumBox.Text.Trim();
        var fileName = FileNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = SongDetailsResult.BuildDefaultFileName(artist, album, title);
        if (string.IsNullOrWhiteSpace(albumArtist) && !string.IsNullOrWhiteSpace(artist))
            albumArtist = MusicFilenameBuilder.PrimaryArtist(artist) ?? artist;

        Result = new SongDetailsResult
        {
            Url = _initial.Url,
            DetectedArtist = _initial.DetectedArtist,
            DetectedAlbumArtist = _initial.DetectedAlbumArtist,
            DetectedAlbum = _initial.DetectedAlbum,
            DetectedTitle = _initial.DetectedTitle,
            DetectedFileName = _initial.DetectedFileName,
            Artist = artist,
            AlbumArtist = albumArtist,
            Album = album,
            Title = title,
            FileName = fileName,
        };

        return true;
    }

    private void UpdateStatusText()
    {
        var edited =
            !string.Equals(TitleBox.Text.Trim(), _initial.DetectedTitle, StringComparison.Ordinal) ||
            !string.Equals(ArtistBox.Text.Trim(), _initial.DetectedArtist, StringComparison.Ordinal) ||
            !string.Equals(AlbumArtistBox.Text.Trim(), _initial.DetectedAlbumArtist, StringComparison.Ordinal) ||
            !string.Equals(AlbumBox.Text.Trim(), _initial.DetectedAlbum, StringComparison.Ordinal) ||
            _fileNameManuallyEdited;

        StatusText.Text = edited
            ? "Manual edits will be embedded in the file tags."
            : "Using detected details — edit any field before downloading.";
    }

    private static string BuildDetectedHint(SongDetailsResult details)
    {
        if (string.IsNullOrWhiteSpace(details.DetectedTitle) &&
            string.IsNullOrWhiteSpace(details.DetectedArtist))
            return "Could not auto-detect much — fill in the fields manually.";

        return $"Detected from YouTube: {details.DetectedArtist} — {details.DetectedTitle}" +
               (string.IsNullOrWhiteSpace(details.DetectedAlbum) ? "" : $" ({details.DetectedAlbum})");
    }
}
