using System.Windows.Threading;

namespace YouTubeToMp3.Services;

/// <summary>
/// When <see cref="BackgroundRunMode.IdleTimeout"/> is enabled, quits the app after inactivity.
/// </summary>
public sealed class AppIdleMonitor : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly Func<bool> _isBusy;
    private readonly Action _requestExit;
    private DateTime _lastActivityUtc = DateTime.UtcNow;

    public AppIdleMonitor(Dispatcher dispatcher, Func<bool> isBusy, Action requestExit)
    {
        _isBusy = isBusy;
        _requestExit = requestExit;
        _timer = new DispatcherTimer(TimeSpan.FromMinutes(1), DispatcherPriority.Background, OnTick, dispatcher);
    }

    public void ApplySettings()
    {
        _timer.Stop();
        if (App.Settings.BackgroundRunMode == BackgroundRunMode.IdleTimeout)
        {
            NoteActivity();
            _timer.Start();
        }
    }

    public void NoteActivity() => _lastActivityUtc = DateTime.UtcNow;

    private void OnTick(object? sender, EventArgs e) => CheckIdle();

    private void CheckIdle()
    {
        if (App.Settings.BackgroundRunMode != BackgroundRunMode.IdleTimeout)
            return;

        if (_isBusy())
        {
            NoteActivity();
            return;
        }

        var minutes = Math.Clamp(App.Settings.IdleTimeoutMinutes, 5, 120);
        if (DateTime.UtcNow - _lastActivityUtc < TimeSpan.FromMinutes(minutes))
            return;

        _requestExit();
    }

    public void Dispose() => _timer.Stop();
}
