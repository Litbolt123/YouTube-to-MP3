using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace YouTubeToMp3.Services;

public sealed class DownloadProgressEventArgs : EventArgs
{
    public string Line { get; init; } = "";
    public double? Percent { get; init; }
    public string? Eta { get; init; }
    public string? Status { get; init; }
    public string? Speed { get; init; }
    public int? PlaylistIndex { get; init; }
    public int? PlaylistTotal { get; init; }
    public string? CurrentTrackTitle { get; init; }
    public string? CurrentVideoUrl { get; init; }
}

public sealed class DownloadCompletedEventArgs : EventArgs
{
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string OutputPath { get; init; } = "";
    public string? ErrorMessage { get; init; }
    public DownloadFormat Format { get; init; }
    public DownloadScope Scope { get; init; }
    public ContentKind ContentKind { get; init; }
    public string OutputFolder { get; init; } = "";
}

public sealed class YtDlpDownloadService : IDisposable
{
    private static readonly Regex ProgressRegex = new(@"\[download\]\s+(\d+(?:\.\d+)?)%", RegexOptions.Compiled);
    private static readonly Regex EtaRegex = new(@"\bETA\s+(\d+:\d+(?::\d+)?)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SpeedRegex = new(@"at\s+([\d.]+\s*(?:KiB|MiB|GiB|KB|MB|GB)/s)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PlaylistItemRegex = new(@"Downloading (?:item|video)\s+(\d+)\s+of\s+(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DestinationRegex = new(
        @"(?:\[download\]|\[ExtractAudio\]|\[Merger\])\s+Destination:\s+(.+)$",
        RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex DestinationLineRegex = new(
        @"(?:\[download\]|\[ExtractAudio\]|\[Merger\])\s+Destination:\s+(.+)$",
        RegexOptions.Compiled);
    private static readonly Regex ExtractAudioTitleRegex = new(
        @"\[ExtractAudio\]\s+Destination:\s+(.+)$",
        RegexOptions.Compiled);
    private static readonly Regex DownloadTitleRegex = new(
        @"\[download\]\s+Downloading\s+(?:item|video)\s+\d+\s+of\s+\d+:\s+(.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex YoutubeUrlInLineRegex = new(
        @"(https?://(?:www\.)?(?:youtube\.com/watch\?v=|youtu\.be/)[^\s""']+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex YoutubeIdInLineRegex = new(
        @"\[youtube\]\s+([A-Za-z0-9_-]{11}):",
        RegexOptions.Compiled);
    private static readonly Regex MergeRegex = new(@"\[ExtractAudio\]|Deleting original file|\[Merger\]|Embedding thumbnail|\[ThumbnailsConvertor\]", RegexOptions.Compiled);

    private Process? _process;
    private CancellationTokenSource? _cts;

    public event EventHandler<DownloadProgressEventArgs>? Progress;
    public event EventHandler<DownloadCompletedEventArgs>? Completed;

    public bool IsRunning => _process is { HasExited: false };

    public async Task StartAsync(
        string url,
        string outputFolder,
        DownloadFormat format,
        DownloadScope scope,
        ContentKind contentKind,
        int audioQuality,
        bool embedThumbnail,
        string? customFileName = null,
        string? musicArtistFolder = null,
        string? musicAlbumFolder = null,
        TrackMetadataOverride? metadataOverride = null,
        int concurrentFragments = 0,
        bool useDownloadArchive = false,
        string? customCoverArtPath = null,
        CancellationToken cancellationToken = default)
    {
        if (IsRunning)
            throw new InvalidOperationException("A download is already running.");

        var tools = ToolDependencyService.Check();
        if (!tools.AllFound)
            throw new InvalidOperationException($"Missing: {tools.MissingSummary}. {ToolDependencyService.InstallHint}");

        Directory.CreateDirectory(outputFolder);
        url = YouTubeUrlHelper.Normalize(url, scope);
        var useCustomCover = !string.IsNullOrWhiteSpace(customCoverArtPath) && File.Exists(customCoverArtPath);
        var embedYtThumbnail = embedThumbnail && !useCustomCover;
        var outputTemplate = await OutputTemplateBuilder.ResolveAsync(
            outputFolder,
            scope,
            contentKind,
            format,
            url,
            customFileName,
            musicArtistFolder,
            musicAlbumFolder,
            cancellationToken).ConfigureAwait(false);
        var args = BuildArguments(
            url,
            outputTemplate,
            format,
            scope,
            contentKind,
            audioQuality,
            embedYtThumbnail,
            tools,
            metadataOverride,
            concurrentFragments,
            useDownloadArchive);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var allLines = new List<string>();
        var writtenFiles = new List<string>();

        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = tools.YtDlpPath!,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
            EnableRaisingEvents = true,
        };
        ProcessEncoding.ConfigureUtf8(_process.StartInfo);

        var stderr = new List<string>();
        void TrackLine(string? line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;
            allLines.Add(line);
            stderr.Add(line);
            TrackWrittenFile(line, writtenFiles);
        }

        _process.OutputDataReceived += (_, e) =>
        {
            HandleLine(e.Data, format);
            TrackLine(e.Data);
        };
        _process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                HandleLine(e.Data, format);
                TrackLine(e.Data);
            }
        };

        if (!_process.Start())
            throw new InvalidOperationException("Could not start yt-dlp.");

        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        try
        {
            await _process.WaitForExitAsync(_cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!_process.HasExited)
                    _process.Kill(entireProcessTree: true);
            }
            catch
            {
                /* ignore */
            }

            Completed?.Invoke(this, new DownloadCompletedEventArgs
            {
                Success = false,
                ExitCode = -1,
                OutputPath = outputFolder,
                ErrorMessage = "Download cancelled.",
                Format = format,
                Scope = scope,
                ContentKind = contentKind,
                OutputFolder = outputFolder,
            });
            return;
        }

        var success = _process.ExitCode == 0;
        var combined = string.Join(Environment.NewLine, allLines);
        var destMatch = DestinationRegex.Match(combined);
        var outputFile = destMatch.Success ? destMatch.Groups[1].Value.Trim() : outputFolder;

        string? coverError = null;
        if (success && useCustomCover)
        {
            try
            {
                Progress?.Invoke(this, new DownloadProgressEventArgs
                {
                    Line = "Embedding custom cover art…",
                    Status = "Embedding custom cover art…",
                });

                var targets = writtenFiles
                    .Where(CoverArtEmbedService.IsEmbeddableMedia)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (targets.Count == 0 && CoverArtEmbedService.IsEmbeddableMedia(outputFile))
                    targets.Add(outputFile);

                await CoverArtEmbedService.EmbedIntoManyAsync(targets, customCoverArtPath!, _cts.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Completed?.Invoke(this, new DownloadCompletedEventArgs
                {
                    Success = false,
                    ExitCode = -1,
                    OutputPath = outputFolder,
                    ErrorMessage = "Download cancelled.",
                    Format = format,
                    Scope = scope,
                    ContentKind = contentKind,
                    OutputFolder = outputFolder,
                });
                return;
            }
            catch (Exception ex)
            {
                coverError = ex.Message;
                success = false;
            }
        }

        Completed?.Invoke(this, new DownloadCompletedEventArgs
        {
            Success = success,
            ExitCode = coverError is null ? _process.ExitCode : -2,
            OutputPath = outputFile,
            ErrorMessage = success
                ? null
                : coverError ?? BuildErrorMessage(stderr, _process.ExitCode),
            Format = format,
            Scope = scope,
            ContentKind = contentKind,
            OutputFolder = outputFolder,
        });
    }

    private static void TrackWrittenFile(string line, List<string> writtenFiles)
    {
        var destMatch = DestinationLineRegex.Match(line);
        if (!destMatch.Success)
            destMatch = ExtractAudioTitleRegex.Match(line);
        if (!destMatch.Success)
            return;

        var path = destMatch.Groups[1].Value.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(path))
            return;
        if (!writtenFiles.Contains(path, StringComparer.OrdinalIgnoreCase))
            writtenFiles.Add(path);
    }

    private static string BuildArguments(
        string url,
        string outputTemplate,
        DownloadFormat format,
        DownloadScope scope,
        ContentKind contentKind,
        int audioQuality,
        bool embedThumbnail,
        ToolCheckResult tools,
        TrackMetadataOverride? metadataOverride,
        int concurrentFragments,
        bool useDownloadArchive)
    {
        var sb = new StringBuilder();
        sb.Append("--newline --progress --ignore-errors ");
        sb.Append("--output-na-placeholder \"\" ");
        sb.Append("--embed-metadata ");
        AppendMetadataOverrides(sb, metadataOverride);
        AppendYouTubeReliabilityArgs(sb, tools, url, format);
        sb.Append($"-o \"{outputTemplate}\" ");

        if (scope == DownloadScope.SingleVideo)
            sb.Append("--no-playlist ");

        if (scope == DownloadScope.Playlist)
        {
            // Embed album tag from playlist title when missing (e.g. YouTube Music albums).
            sb.Append("--parse-metadata \"+%(album)s:%(playlist_title)s\" ");
            if (contentKind == ContentKind.Music && string.IsNullOrWhiteSpace(metadataOverride?.AlbumArtist))
            {
                // Album artist = playlist uploader; track artist stays per-video (featured artists preserved).
                sb.Append("--parse-metadata \"+%(album_artist)s:%(playlist_uploader|playlist_channel|uploader)s\" ");
                sb.Append("--parse-metadata \"+%(meta_album_artist)s:%(playlist_uploader|playlist_channel|uploader)s\" ");
            }
        }
        else if (contentKind == ContentKind.Music && YouTubeUrlHelper.TryGetListId(url) is not null)
        {
            // Single track from an album URL — still map playlist title to album for tags + filename.
            sb.Append("--parse-metadata \"+%(album)s:%(playlist_title)s\" ");
            if (string.IsNullOrWhiteSpace(metadataOverride?.AlbumArtist))
            {
                sb.Append("--parse-metadata \"+%(album_artist)s:%(playlist_uploader|playlist_channel|uploader)s\" ");
                sb.Append("--parse-metadata \"+%(meta_album_artist)s:%(playlist_uploader|playlist_channel|uploader)s\" ");
            }
        }

        if (useDownloadArchive)
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            sb.Append($"--download-archive \"{AppPaths.DownloadArchivePath}\" ");
        }

        if (concurrentFragments > 1)
            sb.Append($"--concurrent-fragments {concurrentFragments} ");

        if (DownloadFormats.IsAudio(format))
        {
            sb.Append("-f bestaudio/best ");
            sb.Append($"-x --audio-format {DownloadFormats.ToYtDlpAudioFormat(format)} --audio-quality {audioQuality} ");
        }
        else
        {
            var formatSelector = QualityPresets.GetMp4FormatSelector(audioQuality);
            sb.Append($"-f \"{formatSelector}\" ");
            sb.Append("--merge-output-format mp4 ");
            // Re-encode to AAC if only Opus audio is available — Windows Media Player cannot play Opus in MP4.
            sb.Append("--postprocessor-args \"Merger+ffmpeg_o:-c:a aac -b:a 192k\" ");
        }

        if (embedThumbnail)
        {
            sb.Append("--embed-thumbnail --convert-thumbnails jpg ");
        }

        sb.Append($"\"{url}\"");
        return sb.ToString();
    }

    private static void AppendMetadataOverrides(StringBuilder sb, TrackMetadataOverride? metadataOverride)
    {
        if (metadataOverride is null || !metadataOverride.HasAny)
            return;

        // Literal values need a leading space on both sides of --parse-metadata so yt-dlp
        // does not treat the first word as a field name / template. Prefer meta_* for --embed-metadata.
        if (!string.IsNullOrWhiteSpace(metadataOverride.Artist))
            AppendLiteralMetadata(sb, metadataOverride.Artist, "artist", "meta_artist");
        if (!string.IsNullOrWhiteSpace(metadataOverride.AlbumArtist))
            AppendLiteralMetadata(sb, metadataOverride.AlbumArtist, "album_artist", "meta_album_artist");
        if (!string.IsNullOrWhiteSpace(metadataOverride.Album))
            AppendLiteralMetadata(sb, metadataOverride.Album, "album", "meta_album");
        if (!string.IsNullOrWhiteSpace(metadataOverride.Title))
            AppendLiteralMetadata(sb, metadataOverride.Title, "title", "meta_title");
    }

    private static void AppendLiteralMetadata(StringBuilder sb, string value, string infoField, string metaField)
    {
        var escaped = EscapeYtDlpValue(value.Trim());
        sb.Append($"--parse-metadata \" {escaped}: %({infoField})s\" ");
        sb.Append($"--parse-metadata \" {escaped}: %({metaField})s\" ");
    }

    private static string EscapeYtDlpValue(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    private static void AppendYouTubeReliabilityArgs(StringBuilder sb, ToolCheckResult tools, string url, DownloadFormat format)
    {
        YouTubeExtractorArgs.Append(sb, tools, url, format);
    }

    private static string BuildErrorMessage(IReadOnlyList<string> stderr, int exitCode)
    {
        var last = stderr.LastOrDefault() ?? $"yt-dlp exited with code {exitCode}.";
        var combined = string.Join('\n', stderr);
        if (combined.Contains("403", StringComparison.Ordinal) &&
            combined.Contains("Forbidden", StringComparison.OrdinalIgnoreCase))
        {
            return last + Environment.NewLine + Environment.NewLine + ToolDependencyService.YouTube403Hint;
        }

        if (combined.Contains("Only images are available", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("Requested format is not available", StringComparison.OrdinalIgnoreCase))
        {
            return last + Environment.NewLine + Environment.NewLine + ToolDependencyService.YouTubeMusicFormatHint;
        }

        return last;
    }

    public void Cancel()
    {
        try
        {
            _cts?.Cancel();
            if (_process is { HasExited: false })
                _process.Kill(entireProcessTree: true);
        }
        catch
        {
            /* ignore */
        }
    }

    private void HandleLine(string? line, DownloadFormat format)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        double? percent = null;
        var match = ProgressRegex.Match(line);
        if (match.Success && double.TryParse(match.Groups[1].Value, out var p))
            percent = p;

        string? eta = null;
        var etaMatch = EtaRegex.Match(line);
        if (etaMatch.Success)
            eta = etaMatch.Groups[1].Value;

        string? speed = null;
        var speedMatch = SpeedRegex.Match(line);
        if (speedMatch.Success)
            speed = speedMatch.Groups[1].Value.Trim();

        int? playlistIndex = null;
        int? playlistTotal = null;
        var playlistMatch = PlaylistItemRegex.Match(line);
        if (playlistMatch.Success &&
            int.TryParse(playlistMatch.Groups[1].Value, out var idx) &&
            int.TryParse(playlistMatch.Groups[2].Value, out var total))
        {
            playlistIndex = idx;
            playlistTotal = total;
        }

        string? currentTrackTitle = null;
        var titledItem = DownloadTitleRegex.Match(line);
        if (titledItem.Success)
            currentTrackTitle = QueueDisplayHelper.CleanTrackTitle(titledItem.Groups[1].Value);
        else
        {
            var destMatch = DestinationLineRegex.Match(line);
            if (!destMatch.Success)
                destMatch = ExtractAudioTitleRegex.Match(line);
            if (destMatch.Success)
                currentTrackTitle = QueueDisplayHelper.CleanTrackTitle(destMatch.Groups[1].Value);
        }

        string? currentVideoUrl = null;
        var urlMatch = YoutubeUrlInLineRegex.Match(line);
        if (urlMatch.Success)
            currentVideoUrl = urlMatch.Groups[1].Value.Trim();
        else
        {
            var idMatch = YoutubeIdInLineRegex.Match(line);
            if (idMatch.Success)
                currentVideoUrl = $"https://www.youtube.com/watch?v={idMatch.Groups[1].Value}";
        }

        string? status = null;
        if (MergeRegex.IsMatch(line))
            status = DownloadFormats.IsAudio(format)
                ? (line.Contains("thumbnail", StringComparison.OrdinalIgnoreCase) ? "Embedding cover art…" : $"Converting to {DownloadFormats.ToDisplayLabel(format)}…")
                : (line.Contains("thumbnail", StringComparison.OrdinalIgnoreCase) ? "Embedding cover art…" : "Merging video…");
        else if (playlistIndex is not null && playlistTotal is not null)
            status = eta is not null && percent is not null
                ? $"Item {playlistIndex}/{playlistTotal} · {percent:0.#}% · ETA {eta}"
                : $"Downloading playlist item {playlistIndex}/{playlistTotal}…";
        else if (line.Contains("Downloading", StringComparison.OrdinalIgnoreCase))
            status = eta is not null ? $"Downloading… ETA {eta}" : "Downloading…";
        else if (line.StartsWith("[download]", StringComparison.Ordinal))
            status = eta is not null && percent is not null
                ? $"Downloading… {percent:0.#}% · ETA {eta}"
                : line;

        Progress?.Invoke(this, new DownloadProgressEventArgs
        {
            Line = line,
            Percent = percent,
            Eta = eta,
            Speed = speed,
            PlaylistIndex = playlistIndex,
            PlaylistTotal = playlistTotal,
            CurrentTrackTitle = currentTrackTitle,
            CurrentVideoUrl = currentVideoUrl,
            Status = status,
        });
    }

    public void Dispose()
    {
        Cancel();
        _process?.Dispose();
        _cts?.Dispose();
    }
}
