using System.Globalization;

namespace YouTubeToMp3.Services;

public enum DownloadPhase
{
    Starting,
    Downloading,
    Merging,
    EmbeddingCover,
    Finishing,
}

public sealed class EtaSmoother
{
    private readonly double _alpha;
    private readonly double _maxJumpSeconds;
    private double? _smoothedSeconds;
    private DownloadPhase _phase = DownloadPhase.Starting;

    public EtaSmoother(double alpha = 0.18, double maxJumpSeconds = 12)
    {
        _alpha = Math.Clamp(alpha, 0.01, 1);
        _maxJumpSeconds = Math.Max(1, maxJumpSeconds);
    }

    public DownloadPhase Phase => _phase;

    public void Reset()
    {
        _smoothedSeconds = null;
        _phase = DownloadPhase.Starting;
    }

    public void SetPhase(DownloadPhase phase)
    {
        if (_phase == phase)
            return;
        _phase = phase;
        // why: phase changes invalidate the previous countdown scale
        _smoothedSeconds = null;
    }

    public TimeSpan? Observe(TimeSpan? rawRemaining)
    {
        if (rawRemaining is null || rawRemaining <= TimeSpan.Zero)
            return Remaining;

        var sample = rawRemaining.Value.TotalSeconds;
        if (_smoothedSeconds is null)
        {
            _smoothedSeconds = sample;
            return Remaining;
        }

        var delta = sample - _smoothedSeconds.Value;
        if (Math.Abs(delta) > _maxJumpSeconds)
            sample = _smoothedSeconds.Value + Math.Sign(delta) * _maxJumpSeconds;

        _smoothedSeconds = (_alpha * sample) + ((1 - _alpha) * _smoothedSeconds.Value);
        if (_smoothedSeconds < 0)
            _smoothedSeconds = 0;

        return Remaining;
    }

    public TimeSpan? Remaining =>
        _smoothedSeconds is { } s ? TimeSpan.FromSeconds(s) : null;

    public string? RemainingLabel =>
        Remaining is { } r ? QueueDisplayHelper.FormatDuration(r) : null;

    public static TimeSpan? CombinePhases(TimeSpan? downloadRemaining, TimeSpan? mergeEstimate, TimeSpan? coverEstimate)
    {
        var total = TimeSpan.Zero;
        var any = false;
        foreach (var part in new[] { downloadRemaining, mergeEstimate, coverEstimate })
        {
            if (part is not { } value || value <= TimeSpan.Zero)
                continue;
            total += value;
            any = true;
        }

        return any ? total : null;
    }

    public static string Format(TimeSpan? value) =>
        value is { } v && v > TimeSpan.Zero
            ? QueueDisplayHelper.FormatDuration(v)
            : "";
}
