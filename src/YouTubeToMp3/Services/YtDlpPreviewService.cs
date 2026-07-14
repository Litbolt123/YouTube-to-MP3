using System.Diagnostics;
using System.Text;

namespace YouTubeToMp3.Services;

public sealed class PreviewResult
{
    public string FileName { get; init; } = "";
    public string FullPath { get; init; } = "";
    public string Title { get; init; } = "";
}

public static class YtDlpPreviewService
{
    public static async Task<PreviewResult?> GetPreviewAsync(
        string url,
        DownloadScope scope,
        DownloadFormat format,
        ContentKind contentKind,
        string outputFolder,
        CancellationToken cancellationToken = default)
    {
        if (contentKind == ContentKind.Music && scope == DownloadScope.SingleVideo)
        {
            var baseName = await YtDlpMetadataService.ResolveMusicBaseNameAsync(
                url, scope, format, contentKind, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(baseName))
            {
                var sanitized = OutputTemplateBuilder.SanitizeFileName(baseName);
                var ext = DownloadFormats.FileExtension(format);
                var fileName = sanitized + ext;
                return new PreviewResult
                {
                    FileName = fileName,
                    FullPath = Path.Combine(outputFolder, fileName),
                    Title = sanitized,
                };
            }
        }

        return await GetYtDlpFilenamePreviewAsync(url, scope, format, contentKind, outputFolder, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<PreviewResult?> GetYtDlpFilenamePreviewAsync(
        string url,
        DownloadScope scope,
        DownloadFormat format,
        ContentKind contentKind,
        string outputFolder,
        CancellationToken cancellationToken)
    {
        var tools = ToolDependencyService.Check();
        if (!tools.AllFound || tools.YtDlpPath is null)
            return null;

        url = YouTubeUrlHelper.Normalize(url, scope);
        var template = await OutputTemplateBuilder.ResolveAsync(
            outputFolder,
            scope,
            contentKind,
            format,
            url,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var args = BuildPreviewArgs(url, template, format, scope, contentKind, tools);

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

            var lines = stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var fullPath = lines.LastOrDefault(static l => !string.IsNullOrWhiteSpace(l))?.Trim();
            if (string.IsNullOrWhiteSpace(fullPath))
                return null;

            return new PreviewResult
            {
                FullPath = fullPath,
                FileName = Path.GetFileName(fullPath),
                Title = Path.GetFileNameWithoutExtension(fullPath),
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

    private static string BuildPreviewArgs(
        string url,
        string template,
        DownloadFormat format,
        DownloadScope scope,
        ContentKind contentKind,
        ToolCheckResult tools)
    {
        var sb = new StringBuilder();
        sb.Append("--no-download --no-warnings --print filename ");
        sb.Append("--output-na-placeholder \"\" ");
        YouTubeExtractorArgs.Append(sb, tools, url, format);
        sb.Append($"-o \"{template}\" ");

        if (scope == DownloadScope.SingleVideo)
            sb.Append("--no-playlist ");
        else if (contentKind == ContentKind.Music)
            sb.Append("--parse-metadata \"+%(album)s:%(playlist_title)s\" ");

        if (DownloadFormats.IsAudio(format))
            sb.Append($"-f bestaudio/best -x --audio-format {DownloadFormats.ToYtDlpAudioFormat(format)} ");
        else
            sb.Append("-f best ");

        sb.Append($"\"{url}\"");
        return sb.ToString();
    }
}
