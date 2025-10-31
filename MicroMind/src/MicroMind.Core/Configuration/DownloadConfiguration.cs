using System.ComponentModel.DataAnnotations;

namespace MicroMind.Core.Configuration;

public class DownloadConfiguration
{
    [Range(0, 10)]
    public int MaxRetries { get; set; } = 3;

    [Range(100, 60000)]
    public int RetryDelayMs { get; set; } = 1000;

    [Range(30, 3600)]
    public int TimeoutSeconds { get; set; } = 600;

    public bool UseExponentialBackoff { get; set; } = true;
}
