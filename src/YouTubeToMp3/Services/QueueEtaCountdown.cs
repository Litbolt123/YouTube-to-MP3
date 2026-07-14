namespace YouTubeToMp3.Services;

/// <summary>Smooth countdown between yt-dlp ETA updates.</summary>
public sealed class QueueEtaCountdown
{
    private DateTime? _deadlineUtc;

    public void Reset() => _deadlineUtc = null;

    public void Sync(TimeSpan? freshEstimate, double resyncThresholdSeconds = 4)
    {
        if (freshEstimate is null || freshEstimate <= TimeSpan.Zero)
        {
            Reset();
            return;
        }

        var now = DateTime.UtcNow;
        if (_deadlineUtc is null)
        {
            _deadlineUtc = now + freshEstimate.Value;
            return;
        }

        var current = _deadlineUtc.Value - now;
        if (current < TimeSpan.Zero)
            current = TimeSpan.Zero;

        if (Math.Abs((current - freshEstimate.Value).TotalSeconds) > resyncThresholdSeconds)
            _deadlineUtc = now + freshEstimate.Value;
    }

    public TimeSpan? Remaining
    {
        get
        {
            if (_deadlineUtc is null)
                return null;

            var remaining = _deadlineUtc.Value - DateTime.UtcNow;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }
}
