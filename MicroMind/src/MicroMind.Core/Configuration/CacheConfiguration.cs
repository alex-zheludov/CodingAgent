namespace MicroMind.Core.Configuration;

public class CacheConfiguration
{
    public string? CachePath { get; set; }

    public bool EnableValidation { get; set; } = true;

    public static string GetDefaultCachePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "MicroMind", "models");
    }
}
