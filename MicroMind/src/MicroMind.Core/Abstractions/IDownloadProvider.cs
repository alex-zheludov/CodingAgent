using MicroMind.Core.Models;

namespace MicroMind.Core.Abstractions;

public interface IDownloadProvider
{
    Task DownloadAsync(string sourceUrl, string targetPath, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default);

    Task<bool> ValidateChecksumAsync(string filePath, string expectedChecksum, string checksumAlgorithm = "SHA256", CancellationToken cancellationToken = default);
}
