using System.Collections.Concurrent;
using System.Net.Http;
using System.Windows.Media.Imaging;

namespace YouTubeToMp3.Services;

public static class CoverArtPreviewService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly string CacheDir = Path.Combine(Path.GetTempPath(), "YouTubeToMp3-cover-preview");

    static CoverArtPreviewService() => Directory.CreateDirectory(CacheDir);

    public static async Task<string?> ResolveDisplayPathAsync(
        string? customCoverPath,
        string? videoUrl,
        string? fallbackUrl,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(customCoverPath) && File.Exists(customCoverPath))
            return customCoverPath;

        if (!string.IsNullOrWhiteSpace(videoUrl))
        {
            var path = await GetCachedThumbnailPathAsync(videoUrl, cancellationToken).ConfigureAwait(false);
            if (path is not null)
                return path;
        }

        if (!string.IsNullOrWhiteSpace(fallbackUrl) &&
            !string.Equals(fallbackUrl, videoUrl, StringComparison.OrdinalIgnoreCase))
        {
            return await GetCachedThumbnailPathAsync(fallbackUrl, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    public static BitmapImage? LoadBitmap(string? path, int decodeWidth = 256)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = decodeWidth;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> GetCachedThumbnailPathAsync(string url, CancellationToken cancellationToken)
    {
        var key = NormalizeCacheKey(url);
        if (Cache.TryGetValue(key, out var cached) && File.Exists(cached))
            return cached;

        var thumbUrl = await CoverArtFetchService.GetThumbnailUrlAsync(url, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(thumbUrl))
            return null;

        var ext = thumbUrl.Contains(".png", StringComparison.OrdinalIgnoreCase) ? ".png" : ".jpg";
        var dest = Path.Combine(CacheDir, $"{key}{ext}");
        if (File.Exists(dest))
        {
            Cache[key] = dest;
            return dest;
        }

        try
        {
            await using var stream = await Http.GetStreamAsync(thumbUrl, cancellationToken).ConfigureAwait(false);
            await using var file = File.Create(dest);
            await stream.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
            Cache[key] = dest;
            return dest;
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeCacheKey(string url)
    {
        var id = YouTubeUrlHelper.TryGetVideoId(url);
        if (!string.IsNullOrWhiteSpace(id))
            return id;

        var listId = YouTubeUrlHelper.TryGetListId(url);
        if (!string.IsNullOrWhiteSpace(listId))
            return "list-" + listId;

        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(url.Trim())))[..16];
    }
}
