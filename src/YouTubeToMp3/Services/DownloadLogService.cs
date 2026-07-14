using System.Text;

namespace YouTubeToMp3.Services;

/// <summary>Writes download output to disk under %LocalAppData%\YouTubeToMp3\logs\.</summary>
public sealed class DownloadLogService : IDisposable
{
    private readonly object _lock = new();
    private StreamWriter? _writer;
    private string? _currentLogPath;

    public string? CurrentLogPath => _currentLogPath;

    public void BeginSession(string summary)
    {
        lock (_lock)
        {
            Directory.CreateDirectory(AppPaths.LogsDirectory);
            _currentLogPath = Path.Combine(
                AppPaths.LogsDirectory,
                $"download-{DateTime.Now:yyyyMMdd-HHmmss}.log");

            _writer?.Dispose();
            _writer = new StreamWriter(_currentLogPath, append: false, Encoding.UTF8)
            {
                AutoFlush = true,
            };

            WriteLine($"=== {summary} ===");
            WriteLine($"Log file: {_currentLogPath}");
        }
    }

    public void WriteLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        lock (_lock)
        {
            _writer?.WriteLine($"[{DateTime.Now:HH:mm:ss}] {line}");
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}
