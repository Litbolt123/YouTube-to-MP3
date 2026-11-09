using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace YouTubeToMp3.Services;

/// <summary>
/// Notifies Local Music Hub immediately when a music download completes (push ingest).
/// Falls back silently — folder watch still imports if push fails.
/// </summary>
public static class LocalMusicHubPushIngest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(4) };

    public static async Task TryNotifyMusicCompletedAsync(
        string path,
        string? sourceUrl,
        bool importFolder = false)
    {
        if (!LocalMusicHubIntegration.IsHubRunning())
            return;

        var status = LocalMusicHubIntegration.GetLinkStatus();
        if (!status.Linked)
            return;

        var hub = TryReadHubIngestSettings();
        if (hub is null || !hub.IntegrateYouTubeDownloader || !hub.LibraryIngestEnabled)
            return;

        if (string.IsNullOrWhiteSpace(hub.LibraryIngestToken))
            return;

        var port = hub.LibraryIngestPort is > 0 and < 65536
            ? hub.LibraryIngestPort
            : 47385;

        try
        {
            var payload = new
            {
                path = Path.GetFullPath(path),
                contentKind = "music",
                sourceUrl,
                completedUtc = DateTime.UtcNow.ToString("o"),
                importFolder,
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/library/ingest");
            request.Headers.Add("X-Hub-Token", hub.LibraryIngestToken);
            request.Content = new StringContent(
                JsonSerializer.Serialize(payload, JsonOptions),
                Encoding.UTF8,
                "application/json");

            using var response = await Http.SendAsync(request).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
                return;
        }
        catch
        {
            /* folder watch / import-request.json remain fallbacks */
        }
    }

    private static LocalMusicHubIngestSettingsSnapshot? TryReadHubIngestSettings()
    {
        try
        {
            if (!File.Exists(LocalMusicHubIntegration.LocalMusicHubSettingsPath))
                return null;

            var json = File.ReadAllText(LocalMusicHubIntegration.LocalMusicHubSettingsPath);
            return JsonSerializer.Deserialize<LocalMusicHubIngestSettingsSnapshot>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
        }
        catch
        {
            return null;
        }
    }
}

internal sealed class LocalMusicHubIngestSettingsSnapshot
{
    public bool IntegrateYouTubeDownloader { get; set; } = true;
    public bool LibraryIngestEnabled { get; set; } = true;
    public int LibraryIngestPort { get; set; } = 47385;
    public string? LibraryIngestToken { get; set; }
}
