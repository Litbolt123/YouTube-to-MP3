namespace YouTubeToMp3.Services;

public static class SingleInstanceService
{
    public const string MutexName = @"Global\YouTubeToMp3_SingleInstance";

    public static string ActivateSignalPath =>
        Path.Combine(AppPaths.DataDirectory, "activate.signal");

    public static bool TryBecomePrimaryInstance(out Mutex? mutex)
    {
        mutex = new Mutex(true, MutexName, out var createdNew);
        if (createdNew)
            return true;

        mutex.Dispose();
        mutex = null;
        return false;
    }

    public static void NotifyPrimaryInstance()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            File.WriteAllText(ActivateSignalPath, DateTime.UtcNow.ToString("o"));
        }
        catch
        {
            /* ignore */
        }
    }
}
