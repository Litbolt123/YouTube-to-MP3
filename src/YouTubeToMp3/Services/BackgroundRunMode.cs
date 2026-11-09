namespace YouTubeToMp3.Services;

public enum BackgroundRunMode
{
    /// <summary>Keep the tray app running (default).</summary>
    AlwaysOn = 0,

    /// <summary>Quit after a period with no downloads, queue work, or extension API use.</summary>
    IdleTimeout = 1,
}
