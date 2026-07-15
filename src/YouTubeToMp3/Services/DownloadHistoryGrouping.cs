using System.Globalization;

namespace YouTubeToMp3.Services;

/// <summary>Relative date buckets for download history (newest groups first).</summary>
public static class DownloadHistoryGrouping
{
    public static (string Label, int Order) GetGroup(DateTime localCompleted)
    {
        var today = DateTime.Today;
        var date = localCompleted.Date;
        var days = Math.Max(0, (today - date).Days);

        if (days == 0)
            return ("Today", 0);
        if (days == 1)
            return ("Yesterday", 1);

        var weekStart = StartOfWeek(today, DayOfWeek.Monday);
        if (date >= weekStart && days >= 2)
            return ("This week", 2);

        var lastWeekStart = weekStart.AddDays(-7);
        if (date >= lastWeekStart && date < weekStart)
            return ("Last week", 3);

        if (date.Year == today.Year && date.Month == today.Month)
            return ("Earlier this month", 4);

        if (date.Year == today.Year)
            return (date.ToString("MMMM", CultureInfo.CurrentCulture), 100 - date.Month);

        if (date.Year == today.Year - 1)
            return (date.ToString("MMMM yyyy", CultureInfo.CurrentCulture), 200 + (12 - date.Month));

        return (date.ToString("MMMM yyyy", CultureInfo.CurrentCulture), 10_000 - (date.Year * 12 + date.Month));
    }

    public static bool TryParseCompletedUtc(string? completedUtc, out DateTime localCompleted)
    {
        localCompleted = default;
        if (string.IsNullOrWhiteSpace(completedUtc))
            return false;

        if (!DateTime.TryParse(completedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var utc))
            return false;

        localCompleted = utc.ToLocalTime();
        return true;
    }

    private static DateTime StartOfWeek(DateTime date, DayOfWeek start)
    {
        var diff = (7 + (date.DayOfWeek - start)) % 7;
        return date.AddDays(-diff).Date;
    }
}
