using System.Diagnostics;
using System.Text;

namespace YouTubeToMp3.Services;

/// <summary>Ensures yt-dlp / Python emit UTF-8 so titles like “Équinoxe” survive in the UI.</summary>
public static class ProcessEncoding
{
    public static void ConfigureUtf8(ProcessStartInfo startInfo)
    {
        if (startInfo.RedirectStandardOutput)
            startInfo.StandardOutputEncoding = Encoding.UTF8;
        if (startInfo.RedirectStandardError)
            startInfo.StandardErrorEncoding = Encoding.UTF8;

        // Required for Environment dictionary access when UseShellExecute is false.
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        startInfo.Environment["PYTHONUTF8"] = "1";
        startInfo.Environment["PYTHONUTF8MODE"] = "1";
    }
}
