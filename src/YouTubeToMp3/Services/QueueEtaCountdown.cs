namespace YouTubeToMp3.Services;

/// <summary>Smooth countdown between yt-dlp ETA updates.</summary>
public sealed class QueueEtaCountdown
{
    private readonly EtaSmoother _smoother = new(alpha: 0.16, maxJumpSeconds: 10);
    private DateTime? _deadlineUtc;

    public void Reset()
    {
        _deadlineUtc = null;
        _smoother.Reset();
    }

    public void Sync(TimeSpan? freshEstimate, double resyncThresholdSeconds = 10)
    {
        if (freshEstimate is null || freshEstimate <= TimeSpan.Zero)
        {
            Reset();
            return;
        }

        var smoothed = _smoother.Observe(freshEstimate) ?? freshEstimate.Value;
        var now = DateTime.UtcNow;
        if (_deadlineUtc is null)
        {
            _deadlineUtc = now + smoothed;
            return;
        }

        var current = _deadlineUtc.Value - now;
        if (current < TimeSpan.Zero)
            current = TimeSpan.Zero;

        if (Math.Abs((current - smoothed).TotalSeconds) > resyncThresholdSeconds)
            _deadlineUtc = now + smoothed;
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
