using System.Diagnostics;
using System.Text;

namespace YouTubeToMp3.Services;

public static class YtDlpMetadataService
{
    private const string FieldSeparator = "|||";

    public static async Task<TrackMetadata?> GetTrackMetadataAsync(
        string url,
        DownloadScope scope,
        DownloadFormat format,
        ContentKind contentKind,
        CancellationToken cancellationToken = default)
    {
        var tools = ToolDependencyService.Check();
        if (!tools.AllFound || tools.YtDlpPath is null)
            return null;

        url = YouTubeUrlHelper.Normalize(url, scope);
        var args = BuildArgs(url, scope, format, contentKind, tools);

        try
        {
            var stdout = await RunYtDlpStdoutAsync(tools.YtDlpPath!, args, cancellationToken).ConfigureAwait(false);
            if (stdout is null)
                return null;

            var line = stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .LastOrDefault(static l => !string.IsNullOrWhiteSpace(l));
            if (string.IsNullOrWhiteSpace(line))
                return null;

            var parts = line.Split(FieldSeparator);
            if (parts.Length < 4)
                return null;

            return new TrackMetadata
            {
                Artist = NullIfBlank(parts[0]),
                Album = NullIfBlank(parts[1]),
                PlaylistTitle = NullIfBlank(parts[2]),
                Title = NullIfBlank(parts[3]),
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    public static async Task<string?> ResolveMusicBaseNameAsync(
        string url,
        DownloadScope scope,
        DownloadFormat format,
        ContentKind contentKind,
        CancellationToken cancellationToken = default)
    {
        if (contentKind != ContentKind.Music || scope != DownloadScope.SingleVideo)
            return null;

        var metadata = await GetTrackMetadataAsync(url, scope, format, contentKind, cancellationToken)
            .ConfigureAwait(false);
        return metadata is null ? null : MusicFilenameBuilder.Format(metadata);
    }

    public static async Task<MusicPlaylistFolderInfo?> GetMusicPlaylistFolderInfoAsync(
        string url,
        DownloadFormat format,
        ContentKind contentKind,
        CancellationToken cancellationToken = default)
    {
        if (contentKind != ContentKind.Music)
            return null;

        var tools = ToolDependencyService.Check();
        if (!tools.AllFound || tools.YtDlpPath is null)
            return null;

        url = YouTubeUrlHelper.Normalize(url, DownloadScope.Playlist);
        var args = BuildPlaylistFolderArgs(url, format, contentKind, tools);

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = tools.YtDlpPath,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };
            ProcessEncoding.ConfigureUtf8(process.StartInfo);

            if (!process.Start())
                return null;

            var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
                return null;

            var line = stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .LastOrDefault(static l => !string.IsNullOrWhiteSpace(l));
            if (string.IsNullOrWhiteSpace(line))
                return null;

            var parts = line.Split(FieldSeparator);
            if (parts.Length < 7)
                return null;

            var artistFolder = MusicFilenameBuilder.ResolvePlaylistArtistFolder(
                NullIfBlank(parts[0]),
                NullIfBlank(parts[1]),
                NullIfBlank(parts[2]),
                NullIfBlank(parts[3]),
                NullIfBlank(parts[4]));
            var albumFolder = MusicFilenameBuilder.ResolvePlaylistAlbumFolder(
                NullIfBlank(parts[5]),
                NullIfBlank(parts[6]));

            return new MusicPlaylistFolderInfo
            {
                ArtistFolder = artistFolder,
                AlbumFolder = albumFolder,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static string BuildPlaylistFolderArgs(
        string url,
        DownloadFormat format,
        ContentKind contentKind,
        ToolCheckResult tools)
    {
        var sb = new StringBuilder();
        sb.Append("--no-download --no-warnings --playlist-items 1 ");
        sb.Append($"--print \"%(playlist_uploader)s{FieldSeparator}%(playlist_channel)s{FieldSeparator}%(uploader)s{FieldSeparator}%(channel)s{FieldSeparator}%(artist)s{FieldSeparator}%(album)s{FieldSeparator}%(playlist_title)s\" ");
        sb.Append("--output-na-placeholder \"\" ");
        YouTubeExtractorArgs.Append(sb, tools, url, format);
        sb.Append("--parse-metadata \"+%(album)s:%(playlist_title)s\" ");

        if (DownloadFormats.IsAudio(format))
            sb.Append($"-f {QualityPresets.BestAudioFormatSelector} -x --audio-format {DownloadFormats.ToYtDlpAudioFormat(format)} ");

        sb.Append($"\"{url}\"");
        return sb.ToString();
    }

    private static string BuildArgs(
        string url,
        DownloadScope scope,
        DownloadFormat format,
        ContentKind contentKind,
        ToolCheckResult tools)
    {
        var sb = new StringBuilder();
        sb.Append("--no-download --no-warnings ");
        sb.Append($"--print \"%(artist)s{FieldSeparator}%(album)s{FieldSeparator}%(playlist_title)s{FieldSeparator}%(title)s\" ");
        sb.Append("--output-na-placeholder \"\" ");
        YouTubeExtractorArgs.Append(sb, tools, url, format);

        if (scope == DownloadScope.SingleVideo)
            sb.Append("--no-playlist ");

        if (contentKind == ContentKind.Music && YouTubeUrlHelper.TryGetListId(url) is not null)
            sb.Append("--parse-metadata \"+%(album)s:%(playlist_title)s\" ");

        if (DownloadFormats.IsAudio(format))
            sb.Append($"-f {QualityPresets.BestAudioFormatSelector} -x --audio-format {DownloadFormats.ToYtDlpAudioFormat(format)} ");

        sb.Append($"\"{url}\"");
        return sb.ToString();
    }

    public static async Task<string?> ResolveCollectionTitleAsync(
        string url,
        DownloadScope scope,
        DownloadFormat format,
        ContentKind contentKind,
        CancellationToken cancellationToken = default)
    {
        if (scope != DownloadScope.Playlist)
            return null;

        if (contentKind == ContentKind.Music)
        {
            var info = await GetMusicPlaylistFolderInfoAsync(url, format, contentKind, cancellationToken)
                .ConfigureAwait(false);
            if (info is not null)
                return MusicFilenameBuilder.FormatCollectionTitle(info.ArtistFolder, info.AlbumFolder);
        }

        return await GetPlaylistTitleAsync(url, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<string?> GetPlaylistTitleAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        var tools = ToolDependencyService.Check();
        if (!tools.AllFound || tools.YtDlpPath is null)
            return null;

        url = YouTubeUrlHelper.Normalize(url, DownloadScope.Playlist);
        var hasListOnly = YouTubeUrlHelper.TryGetListId(url) is not null &&
                          YouTubeUrlHelper.TryGetVideoId(url) is null;
        var args = hasListOnly
            ? $"--print \"%(playlist_title)s\" \"{url}\""
            : $"--print \"%(playlist_title)s\" --no-playlist \"{url}\"";

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = tools.YtDlpPath,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };
            ProcessEncoding.ConfigureUtf8(process.StartInfo);
            if (!process.Start())
                return null;

            var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (process.ExitCode != 0)
                return null;

            var title = stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault(static l => !string.IsNullOrWhiteSpace(l));
            return string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static string? NullIfBlank(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        return trimmed is "+" or "NA" or "None" or "N/A" ? null : trimmed;
    }

    private const int MaxAlbumReviewTracks = 200;

    public static async Task<AlbumReviewResult?> GetAlbumReviewAsync(
        string url,
        DownloadFormat format,
        ContentKind contentKind,
        IProgress<(int Done, int Total, string? Message)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (contentKind != ContentKind.Music)
            return null;

        var tools = ToolDependencyService.Check();
        if (!tools.AllFound || tools.YtDlpPath is null)
            return null;

        url = YouTubeUrlHelper.Normalize(url, DownloadScope.Playlist);
        var folderInfo = await GetMusicPlaylistFolderInfoAsync(url, format, contentKind, cancellationToken)
            .ConfigureAwait(false);
        var collectionTitle = folderInfo is not null
            ? MusicFilenameBuilder.FormatCollectionTitle(folderInfo.ArtistFolder, folderInfo.AlbumFolder)
            : await GetPlaylistTitleAsync(url, cancellationToken).ConfigureAwait(false) ?? "Album";

        progress?.Report((0, 1, "Reading playlist…"));
        var flatEntries = await ReadFlatPlaylistAsync(url, format, tools, cancellationToken).ConfigureAwait(false);
        if (flatEntries.Count == 0)
            return null;

        if (flatEntries.Count > MaxAlbumReviewTracks)
            flatEntries = flatEntries.Take(MaxAlbumReviewTracks).ToList();

        var artistFolder = folderInfo?.ArtistFolder ?? "";
        var albumFolder = folderInfo?.AlbumFolder ?? collectionTitle;
        var tracks = new List<PlaylistTrackPlan>(flatEntries.Count);
        var enrichGate = new SemaphoreSlim(3);
        var done = 0;
        var metadataByIndex = new System.Collections.Concurrent.ConcurrentDictionary<int, TrackMetadata>();

        foreach (var entry in flatEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var videoUrl = YouTubeUrlHelper.BuildTrackUrl(url, entry.VideoId);
            tracks.Add(new PlaylistTrackPlan
            {
                Index = entry.Index,
                VideoId = entry.VideoId,
                VideoUrl = videoUrl,
                Title = $"Track {entry.Index:00}",
                Album = albumFolder,
                DetectedAlbum = albumFolder,
            });
        }

        var enrichTasks = tracks.Select(async track =>
        {
            await enrichGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var metadata = await GetTrackMetadataAsync(
                    track.VideoUrl,
                    DownloadScope.SingleVideo,
                    format,
                    contentKind,
                    cancellationToken).ConfigureAwait(false);
                if (metadata is not null)
                    metadataByIndex[track.Index] = metadata;
            }
            finally
            {
                var completed = Interlocked.Increment(ref done);
                progress?.Report((completed, tracks.Count, track.Title));
                enrichGate.Release();
            }
        }).ToArray();

        await Task.WhenAll(enrichTasks).ConfigureAwait(false);

        foreach (var track in tracks)
        {
            if (!metadataByIndex.TryGetValue(track.Index, out var metadata))
            {
                var flatEntry = flatEntries.FirstOrDefault(e => e.Index == track.Index);
                if (flatEntry is not null && !string.IsNullOrWhiteSpace(flatEntry.Title))
                {
                    var cleaned = MusicTitleCleaner.Clean(flatEntry.Title, albumFolder, collectionTitle);
                    track.DetectedTitle = cleaned;
                    track.Title = cleaned;
                    track.RefreshDetectedFileName();
                }

                continue;
            }

            if (!string.IsNullOrWhiteSpace(metadata.Title))
            {
                var cleaned = MusicTitleCleaner.Clean(metadata.Title, albumFolder, collectionTitle);
                track.DetectedTitle = cleaned;
                track.Title = cleaned;
            }

            var artist = MusicFilenameBuilder.PrimaryArtist(metadata.Artist) ?? metadata.Artist;
            if (!string.IsNullOrWhiteSpace(artist))
            {
                track.DetectedArtist = artist;
                track.Artist = artist;
            }

            var album = !string.IsNullOrWhiteSpace(metadata.Album) ? metadata.Album : metadata.PlaylistTitle;
            if (!string.IsNullOrWhiteSpace(album))
            {
                track.DetectedAlbum = album;
                if (string.IsNullOrWhiteSpace(track.Album) || track.Album == albumFolder)
                    track.Album = album!;
            }

            track.RefreshDetectedFileName();
        }

        return new AlbumReviewResult
        {
            PlaylistUrl = url,
            CollectionTitle = collectionTitle,
            ArtistFolder = artistFolder,
            AlbumFolder = albumFolder,
            Tracks = tracks,
        };
    }

    private sealed record FlatPlaylistEntry(int Index, string VideoId, string? Title);

    private static async Task<List<FlatPlaylistEntry>> ReadFlatPlaylistAsync(
        string url,
        DownloadFormat format,
        ToolCheckResult tools,
        CancellationToken cancellationToken)
    {
        var args = new StringBuilder();
        args.Append("--flat-playlist --ignore-errors --no-warnings ");
        args.Append($"--print \"%(playlist_index)s{FieldSeparator}%(id)s{FieldSeparator}%(title)s\" ");
        args.Append("--output-na-placeholder \"\" ");
        YouTubeExtractorArgs.Append(args, tools, url, format);
        args.Append($"\"{url}\"");

        var stdout = await RunYtDlpStdoutAsync(tools.YtDlpPath!, args.ToString(), cancellationToken)
            .ConfigureAwait(false);
        if (stdout is null)
            return [];

        var entries = new List<FlatPlaylistEntry>();
        var seenVideoIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fallbackIndex = 0;
        foreach (var line in stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split(FieldSeparator);
            if (parts.Length < 2)
                continue;

            fallbackIndex++;
            var index = int.TryParse(parts[0], out var parsedIndex) && parsedIndex > 0
                ? parsedIndex
                : fallbackIndex;

            var id = parts[1].Trim();
            if (string.IsNullOrWhiteSpace(id))
                continue;

            if (!seenVideoIds.Add(id))
                continue;

            var title = parts.Length > 2 ? NullIfBlank(parts[2]) : null;
            entries.Add(new FlatPlaylistEntry(index, id, title));
        }

        return entries.OrderBy(static e => e.Index).ToList();
    }

    private static async Task<TrackMetadata?> GetTrackMetadataForPlaylistItemAsync(
        string playlistUrl,
        int playlistIndex,
        DownloadFormat format,
        ContentKind contentKind,
        ToolCheckResult tools,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.Append($"--no-download --no-warnings --playlist-items {playlistIndex} ");
        sb.Append($"--print \"%(artist)s{FieldSeparator}%(album)s{FieldSeparator}%(playlist_title)s{FieldSeparator}%(title)s\" ");
        sb.Append("--output-na-placeholder \"\" ");
        YouTubeExtractorArgs.Append(sb, tools, playlistUrl, format);
        if (contentKind == ContentKind.Music)
            sb.Append("--parse-metadata \"+%(album)s:%(playlist_title)s\" ");
        if (DownloadFormats.IsAudio(format))
            sb.Append($"-f {QualityPresets.BestAudioFormatSelector} -x --audio-format {DownloadFormats.ToYtDlpAudioFormat(format)} ");
        sb.Append($"\"{playlistUrl}\"");

        var stdout = await RunYtDlpStdoutAsync(tools.YtDlpPath!, sb.ToString(), cancellationToken)
            .ConfigureAwait(false);
        if (stdout is null)
            return null;

        var line = stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(static l => !string.IsNullOrWhiteSpace(l));
        if (string.IsNullOrWhiteSpace(line))
            return null;

        var parts = line.Split(FieldSeparator);
        if (parts.Length < 4)
            return null;

        return new TrackMetadata
        {
            Artist = NullIfBlank(parts[0]),
            Album = NullIfBlank(parts[1]),
            PlaylistTitle = NullIfBlank(parts[2]),
            Title = NullIfBlank(parts[3]),
        };
    }

    private static async Task<string?> RunYtDlpStdoutAsync(
        string ytDlpPath,
        string arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ytDlpPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };
            ProcessEncoding.ConfigureUtf8(process.StartInfo);
            if (!process.Start())
                return null;

            var stdoutTask = ReadStdoutUtf8Async(process.StandardOutput.BaseStream, cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            var stdout = await stdoutTask.ConfigureAwait(false);
            await stderrTask.ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(stdout) ? stdout : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string> ReadStdoutUtf8Async(Stream stream, CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
        return Encoding.UTF8.GetString(ms.ToArray());
    }
}
