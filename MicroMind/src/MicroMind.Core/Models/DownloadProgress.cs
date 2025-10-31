namespace MicroMind.Core.Models;

public class DownloadProgress
{
    public long BytesDownloaded { get; init; }

    public long? TotalBytes { get; init; }

    public double? PercentComplete => TotalBytes.HasValue && TotalBytes.Value > 0
        ? (double)BytesDownloaded / TotalBytes.Value * 100
        : null;

    public double? BytesPerSecond { get; init; }
}
