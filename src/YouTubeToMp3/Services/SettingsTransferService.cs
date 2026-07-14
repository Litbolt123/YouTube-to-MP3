using System.Text.Json;

namespace YouTubeToMp3.Services;

public static class SettingsTransferService
{
    public static bool Export(AppSettings settings, string destinationPath)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, AppSettingsService.SerializerOptions);
            File.WriteAllText(destinationPath, json);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static (AppSettings? settings, string? error) Import(string sourcePath)
    {
        try
        {
            if (!File.Exists(sourcePath))
                return (null, "File not found.");

            var json = File.ReadAllText(sourcePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, AppSettingsService.SerializerOptions);
            if (settings is null)
                return (null, "Could not read settings from file.");

            return (settings, null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }
}
