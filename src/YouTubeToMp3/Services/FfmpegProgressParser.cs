using System.Globalization;
using System.Text.RegularExpressions;

namespace YouTubeToMp3.Services;

public static class FfmpegProgressParser
{
    private static readonly Regex TimeRegex = new(
        @"\btime=(\d{1,3}):(\d{2}):(\d{2}(?:\.\d+)?)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex OutTimeMsRegex = new(
        @"^out_time_ms=(\d+)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    private static readonly Regex OutTimeRegex = new(
        @"^out_time=(\d{1,3}):(\d{2}):(\d{2}(?:\.\d+)?)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    private static readonly Regex DurationRegex = new(
        @"\bDuration:\s*(\d{1,3}):(\d{2}):(\d{2}(?:\.\d+)?)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex YtDlpDurationRegex = new(
        @"^DURATION:(\d+(?:\.\d+)?)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool TryParseTimestamp(string line, out TimeSpan value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(line))
            return false;

        var ms = OutTimeMsRegex.Match(line);
        if (ms.Success && long.TryParse(ms.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var micros))
        {
            // ffmpeg out_time_ms is microseconds despite the name
            value = TimeSpan.FromTicks(micros * 10);
            return value >= TimeSpan.Zero;
        }

        var outTime = OutTimeRegex.Match(line);
        if (outTime.Success)
            return TryParseHms(outTime, out value);

        var time = TimeRegex.Match(line);
        return time.Success && TryParseHms(time, out value);
    }

    public static bool TryParseDuration(string line, out TimeSpan value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(line))
            return false;

        var marked = YtDlpDurationRegex.Match(line.Trim());
        if (marked.Success &&
            double.TryParse(marked.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) &&
            seconds > 0)
        {
            value = TimeSpan.FromSeconds(seconds);
            return true;
        }

        var match = DurationRegex.Match(line);
        if (!match.Success)
            return false;

        if (!TryParseHms(match, out value))
            return false;

        return value > TimeSpan.Zero;
    }

    public static double? ToPercent(TimeSpan position, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero || position < TimeSpan.Zero)
            return null;

        var pct = 100.0 * position.TotalSeconds / duration.TotalSeconds;
        if (double.IsNaN(pct) || double.IsInfinity(pct))
            return null;

        return Math.Clamp(pct, 0, 99.9);
    }

    private static bool TryParseHms(Match match, out TimeSpan value)
    {
        value = default;
        if (!int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var h))
            return false;
        if (!int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var m))
            return false;
        if (!double.TryParse(match.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var s))
            return false;

        value = TimeSpan.FromHours(h) + TimeSpan.FromMinutes(m) + TimeSpan.FromSeconds(s);
        return true;
    }
}
