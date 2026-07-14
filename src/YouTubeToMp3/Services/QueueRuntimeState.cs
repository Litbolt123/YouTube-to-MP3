namespace YouTubeToMp3.Services;

public sealed class QueueRuntimeState
{
    public double? Percent { get; private set; }
    public string? Eta { get; private set; }
    public string? Speed { get; private set; }
    public string? Status { get; private set; }
    public int? PlaylistIndex { get; private set; }
    public int? PlaylistTotal { get; private set; }
    public string? CurrentTrackTitle { get; private set; }
    public string? CurrentVideoUrl { get; private set; }

    private DateTime? _fileEtaDeadlineUtc;

    public event EventHandler? Changed;

    public string? LiveFileEta
    {
        get
        {
            if (_fileEtaDeadlineUtc is null)
                return Eta;

            var remaining = _fileEtaDeadlineUtc.Value - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                return "0:00";

            return QueueDisplayHelper.FormatDuration(remaining);
        }
    }

    public void ApplyProgress(DownloadProgressEventArgs e)
    {
        if (e.Percent is { } p)
            Percent = p;
        if (!string.IsNullOrWhiteSpace(e.Eta) && !string.Equals(e.Eta, Eta, StringComparison.Ordinal))
        {
            Eta = e.Eta;
            var parsed = QueueDisplayHelper.ParseEta(e.Eta);
            _fileEtaDeadlineUtc = parsed is { } fileEta ? DateTime.UtcNow + fileEta : null;
        }
        if (!string.IsNullOrWhiteSpace(e.Speed))
            Speed = e.Speed;
        if (!string.IsNullOrWhiteSpace(e.Status))
            Status = e.Status;
        if (e.PlaylistIndex is not null)
        {
            if (PlaylistIndex is { } prev && e.PlaylistIndex != prev)
            {
                Percent = null;
                Eta = null;
                _fileEtaDeadlineUtc = null;
                Speed = null;
                CurrentTrackTitle = null;
                CurrentVideoUrl = null;
            }

            PlaylistIndex = e.PlaylistIndex;
        }

        if (e.PlaylistTotal is not null)
            PlaylistTotal = e.PlaylistTotal;
        if (!string.IsNullOrWhiteSpace(e.CurrentTrackTitle))
            CurrentTrackTitle = e.CurrentTrackTitle;
        if (!string.IsNullOrWhiteSpace(e.CurrentVideoUrl))
            CurrentVideoUrl = e.CurrentVideoUrl;

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SeedFromJob(DownloadJob job)
    {
        if (job.PlaylistTrackIndex is { } idx)
            PlaylistIndex = idx;
        if (job.PlaylistTrackTotal is { } total)
            PlaylistTotal = total;
        if (!string.IsNullOrWhiteSpace(job.PredictedTitle))
            CurrentTrackTitle = job.PredictedTitle;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Reset()
    {
        Percent = null;
        Eta = null;
        _fileEtaDeadlineUtc = null;
        Speed = null;
        Status = null;
        PlaylistIndex = null;
        PlaylistTotal = null;
        CurrentTrackTitle = null;
        CurrentVideoUrl = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
