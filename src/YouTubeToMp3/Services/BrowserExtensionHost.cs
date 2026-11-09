using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace YouTubeToMp3.Services;

public sealed class ExtensionDownloadRequest : EventArgs
{
    public required string Url { get; init; }
    public DownloadScope Scope { get; init; } = DownloadScope.SingleVideo;
    public DownloadFormat? Format { get; init; }
    public int? Quality { get; init; }
    public ContentKind? ContentKind { get; init; }
    public bool ForceRedownload { get; init; }
}

/// <summary>
/// Local HTTP server for the browser extension and Local Music Hub to queue downloads.
/// Listens on 127.0.0.1 only; requires a shared token from settings.
/// </summary>
public sealed class BrowserExtensionHost : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private string _token = "";
    private bool _enabled;
    private int _configuredPort;

    public event EventHandler<ExtensionDownloadRequest>? DownloadRequested;

    /// <summary>Wired by MainWindow to check queue/history/disk for a URL.</summary>
    public Func<string, DuplicateCheckResult>? CheckUrl { get; set; }

    /// <summary>Wired by MainWindow for live YouTube-button status (queued / downloading / file gone).</summary>
    public Func<string, ExtensionJobStatus>? WatchUrl { get; set; }

    /// <summary>Wired by MainWindow to reset the idle auto-quit timer.</summary>
    public Action? OnActivity { get; set; }

    public bool IsRunning => _listener?.IsListening == true;
    public int Port { get; private set; }

    public void ApplySettings(AppSettings settings)
    {
        AppSettingsService.EnsureExtensionToken(settings);
        var port = settings.BrowserExtensionPort is > 0 and < 65536
            ? settings.BrowserExtensionPort
            : AppSettingsService.DefaultExtensionPort;
        var token = settings.BrowserExtensionToken;
        var enabled = settings.BrowserExtensionEnabled;

        if (IsRunning && _enabled == enabled && _configuredPort == port &&
            string.Equals(_token, token, StringComparison.Ordinal))
        {
            return;
        }

        Stop();
        _enabled = enabled;
        _configuredPort = port;
        _token = token;
        Port = port;

        if (!enabled)
            return;

        Start();
    }

    private void Start()
    {
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        _listener.Start();

        _cts = new CancellationTokenSource();
        _listenTask = Task.Run(() => ListenLoopAsync(_cts.Token));
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener is { IsListening: true })
        {
            try
            {
                var context = await _listener.GetContextAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
                _ = Task.Run(() => HandleRequestAsync(context), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch
            {
                /* keep listening */
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        try
        {
            var request = context.Request;
            var response = context.Response;
            AddCorsHeaders(response);

            if (request.HttpMethod == "OPTIONS")
            {
                response.StatusCode = 204;
                response.Close();
                return;
            }

            var path = request.Url?.AbsolutePath?.TrimEnd('/') ?? "";
            if (path is not "/health" and not "/status")
                OnActivity?.Invoke();

            if (path == "/health" && request.HttpMethod == "GET")
            {
                var hub = LocalMusicHubIntegration.GetLinkStatus();
                var screen = ScreenResolutionService.PreferredVideoHeight;
                await WriteJsonAsync(response, 200, new
                {
                    status = "ok",
                    app = "YouTube Downloader",
                    version = UpdateCheckService.CurrentAssemblyVersion.ToString(3),
                    video = new
                    {
                        matchScreen = App.Settings.MatchVideoToScreen,
                        screenHeight = screen,
                        defaultQuality = QualityPresets.DefaultVideoQuality(App.Settings),
                    },
                    button = new
                    {
                        afterDownload = App.Settings.ExtensionButtonAfterDownload,
                        resetSeconds = App.Settings.ExtensionButtonResetSeconds,
                    },
                    integrations = new
                    {
                        localMusicHub = new
                        {
                            detected = hub.Installed,
                            linked = hub.Linked,
                            watchingMusicFolder = hub.WatchingMusicFolder,
                        },
                    },
                }).ConfigureAwait(false);
                return;
            }

            if (path == "/check" && request.HttpMethod == "POST")
            {
                if (!ValidateToken(request))
                {
                    await WriteJsonAsync(response, 401, new { error = "Invalid token" }).ConfigureAwait(false);
                    return;
                }

                using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
                var body = await reader.ReadToEndAsync().ConfigureAwait(false);
                var payload = JsonSerializer.Deserialize<ExtensionCheckPayload>(body, JsonOptions);

                if (string.IsNullOrWhiteSpace(payload?.Url))
                {
                    await WriteJsonAsync(response, 400, new { error = "Missing url" }).ConfigureAwait(false);
                    return;
                }

                var check = CheckUrl?.Invoke(payload.Url.Trim()) ?? new DuplicateCheckResult
                {
                    Ok = true,
                    Message = "Check unavailable.",
                };

                await WriteJsonAsync(response, 200, new
                {
                    ok = check.Ok,
                    alreadyDownloaded = check.AlreadyDownloaded,
                    inQueue = check.InQueue,
                    inHistory = check.InHistory,
                    message = check.Message,
                    title = check.Title,
                    path = check.Path,
                }).ConfigureAwait(false);
                return;
            }

            if (path == "/status" && request.HttpMethod == "POST")
            {
                if (!ValidateToken(request))
                {
                    await WriteJsonAsync(response, 401, new { error = "Invalid token" }).ConfigureAwait(false);
                    return;
                }

                using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
                var body = await reader.ReadToEndAsync().ConfigureAwait(false);
                var payload = JsonSerializer.Deserialize<ExtensionCheckPayload>(body, JsonOptions);
                if (string.IsNullOrWhiteSpace(payload?.Url))
                {
                    await WriteJsonAsync(response, 400, new { error = "Missing url" }).ConfigureAwait(false);
                    return;
                }

                var status = WatchUrl?.Invoke(payload.Url.Trim()) ?? new ExtensionJobStatus
                {
                    State = "idle",
                    AfterDownload = App.Settings.ExtensionButtonAfterDownload,
                    ResetSeconds = App.Settings.ExtensionButtonResetSeconds,
                };
                await WriteJsonAsync(response, 200, new
                {
                    ok = true,
                    state = status.State,
                    fileExists = status.FileExists,
                    downloadedBefore = status.DownloadedBefore,
                    percent = status.Percent,
                    detail = status.Detail,
                    title = status.Title,
                    path = status.Path,
                    afterDownload = status.AfterDownload,
                    resetSeconds = status.ResetSeconds,
                }).ConfigureAwait(false);
                return;
            }

            if (path == "/download" && request.HttpMethod == "POST")
            {
                if (!ValidateToken(request))
                {
                    await WriteJsonAsync(response, 401, new { error = "Invalid token" }).ConfigureAwait(false);
                    return;
                }

                using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
                var body = await reader.ReadToEndAsync().ConfigureAwait(false);
                var payload = JsonSerializer.Deserialize<ExtensionDownloadPayload>(body, JsonOptions);

                if (string.IsNullOrWhiteSpace(payload?.Url))
                {
                    await WriteJsonAsync(response, 400, new { error = "Missing url" }).ConfigureAwait(false);
                    return;
                }

                var scope = string.Equals(payload.Scope, "playlist", StringComparison.OrdinalIgnoreCase)
                    ? DownloadScope.Playlist
                    : DownloadScope.SingleVideo;

                DownloadFormat? format = null;
                if (!string.IsNullOrWhiteSpace(payload.Format))
                    format = DownloadFormats.FromTag(payload.Format);

                ContentKind? contentKind = null;
                if (!string.IsNullOrWhiteSpace(payload.ContentKind))
                {
                    contentKind = payload.ContentKind.ToLowerInvariant() switch
                    {
                        "music" => Services.ContentKind.Music,
                        "video" => Services.ContentKind.Video,
                        _ => Services.ContentKind.Auto,
                    };
                }

                DownloadRequested?.Invoke(this, new ExtensionDownloadRequest
                {
                    Url = payload.Url.Trim(),
                    Scope = scope,
                    Format = format,
                    Quality = payload.Quality,
                    ContentKind = contentKind,
                    ForceRedownload = payload.ForceRedownload,
                });

                await WriteJsonAsync(response, 200, new { ok = true, message = "Queued" }).ConfigureAwait(false);
                return;
            }

            if (path == "/formats" && request.HttpMethod == "POST")
            {
                if (!ValidateToken(request))
                {
                    await WriteJsonAsync(response, 401, new { error = "Invalid token" }).ConfigureAwait(false);
                    return;
                }

                using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
                var body = await reader.ReadToEndAsync().ConfigureAwait(false);
                var payload = JsonSerializer.Deserialize<ExtensionCheckPayload>(body, JsonOptions);
                if (string.IsNullOrWhiteSpace(payload?.Url))
                {
                    await WriteJsonAsync(response, 400, new { error = "Missing url" }).ConfigureAwait(false);
                    return;
                }

                var probe = await VideoFormatProbeService.ProbeAsync(payload.Url.Trim()).ConfigureAwait(false);
                await WriteJsonAsync(response, 200, new
                {
                    ok = true,
                    heights = probe.Heights,
                    bestHeight = probe.BestHeight,
                    suggestedHeight = probe.SuggestedHeight,
                    screenHeight = probe.ScreenHeight,
                    matchScreen = App.Settings.MatchVideoToScreen,
                    defaultQuality = QualityPresets.DefaultVideoQuality(App.Settings),
                }).ConfigureAwait(false);
                return;
            }

            await WriteJsonAsync(response, 404, new { error = "Not found" }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            try
            {
                await WriteJsonAsync(context.Response, 500, new { error = ex.Message }).ConfigureAwait(false);
            }
            catch
            {
                /* ignore */
            }
        }
    }

    private bool ValidateToken(HttpListenerRequest request)
    {
        var headerToken = request.Headers["X-Extension-Token"];
        if (!string.IsNullOrEmpty(headerToken))
            return string.Equals(headerToken, _token, StringComparison.Ordinal);

        return false;
    }

    private static void AddCorsHeaders(HttpListenerResponse response)
    {
        response.Headers.Add("Access-Control-Allow-Origin", "*");
        response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, X-Extension-Token");
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, int statusCode, object payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        response.StatusCode = statusCode;
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        response.Close();
    }

    public void Stop()
    {
        try
        {
            _cts?.Cancel();
            _listener?.Stop();
            _listener?.Close();
        }
        catch
        {
            /* ignore */
        }

        _listener = null;
        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose() => Stop();

    private sealed class ExtensionCheckPayload
    {
        public string? Url { get; set; }
    }

    private sealed class ExtensionDownloadPayload
    {
        public string? Url { get; set; }
        public string? Scope { get; set; }
        public string? Format { get; set; }
        public int? Quality { get; set; }
        public string? ContentKind { get; set; }
        public bool ForceRedownload { get; set; }
    }
}
