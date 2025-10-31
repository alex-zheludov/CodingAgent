using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MicroMind.Core.Abstractions;
using MicroMind.Core.Configuration;
using MicroMind.Core.Exceptions;
using MicroMind.Core.Models;

namespace MicroMind.Infrastructure;

public class HttpDownloadProvider : IDownloadProvider
{
    private readonly HttpClient _httpClient;
    private readonly IOptions<MicroMindOptions> _options;
    private readonly ILogger<HttpDownloadProvider> _logger;

    public HttpDownloadProvider(
        HttpClient httpClient,
        IOptions<MicroMindOptions> options,
        ILogger<HttpDownloadProvider> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _httpClient.Timeout = TimeSpan.FromSeconds(_options.Value.Download.TimeoutSeconds);
    }

    public async Task DownloadAsync(
        string sourceUrl,
        string targetPath,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var downloadConfig = _options.Value.Download;
        var retryCount = 0;
        var retryDelay = downloadConfig.RetryDelayMs;

        while (true)
        {
            try
            {
                await DownloadCoreAsync(sourceUrl, targetPath, progress, cancellationToken);
                return;
            }
            catch (Exception ex) when (retryCount < downloadConfig.MaxRetries && 
                                      (ex is HttpRequestException || ex is TaskCanceledException))
            {
                retryCount++;
                _logger.LogWarning(ex, 
                    "Download attempt {RetryCount} of {MaxRetries} failed. Retrying in {RetryDelay}ms...",
                    retryCount, downloadConfig.MaxRetries, retryDelay);

                await Task.Delay(retryDelay, cancellationToken);

                if (downloadConfig.UseExponentialBackoff)
                {
                    retryDelay *= 2;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Download failed after {RetryCount} attempts", retryCount);
                throw new ModelDownloadException(
                    $"Failed to download model from {sourceUrl} after {retryCount} attempts. " +
                    $"Please check your internet connection and try again.", ex);
            }
        }
    }

    private async Task DownloadCoreAsync(
        string sourceUrl,
        string targetPath,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(sourceUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        var tempPath = targetPath + ".tmp";

        try
        {
            using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            long totalBytesRead = 0;
            var startTime = DateTime.UtcNow;

            while (true)
            {
                var bytesRead = await contentStream.ReadAsync(buffer, cancellationToken);
                if (bytesRead == 0)
                    break;

                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                totalBytesRead += bytesRead;

                if (progress != null)
                {
                    var elapsed = DateTime.UtcNow - startTime;
                    var bytesPerSecond = elapsed.TotalSeconds > 0 ? totalBytesRead / elapsed.TotalSeconds : 0;

                    progress.Report(new DownloadProgress
                    {
                        BytesDownloaded = totalBytesRead,
                        TotalBytes = totalBytes,
                        BytesPerSecond = bytesPerSecond
                    });
                }
            }

            await fileStream.FlushAsync(cancellationToken);
            fileStream.Close();

            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }
            File.Move(tempPath, targetPath);

            _logger.LogInformation("Successfully downloaded {TotalBytes} bytes to {TargetPath}", 
                totalBytesRead, targetPath);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete temporary file {TempPath}", tempPath);
                }
            }
            throw;
        }
    }

    public async Task<bool> ValidateChecksumAsync(
        string filePath,
        string expectedChecksum,
        string checksumAlgorithm = "SHA256",
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {filePath}", filePath);
        }

        _logger.LogInformation("Validating checksum for {FilePath} using {Algorithm}", 
            filePath, checksumAlgorithm);

        using HashAlgorithm hashAlgorithm = checksumAlgorithm.ToUpperInvariant() switch
        {
            "SHA256" => SHA256.Create(),
            "SHA512" => SHA512.Create(),
            "MD5" => MD5.Create(),
            _ => throw new ArgumentException($"Unsupported checksum algorithm: {checksumAlgorithm}", nameof(checksumAlgorithm))
        };

        using var stream = File.OpenRead(filePath);
        var hashBytes = await hashAlgorithm.ComputeHashAsync(stream, cancellationToken);
        var actualChecksum = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        var expectedChecksumNormalized = expectedChecksum.Replace("-", "").ToLowerInvariant();

        var isValid = actualChecksum == expectedChecksumNormalized;
        
        if (!isValid)
        {
            _logger.LogWarning(
                "Checksum validation failed. Expected: {Expected}, Actual: {Actual}",
                expectedChecksumNormalized, actualChecksum);
        }

        return isValid;
    }
}
