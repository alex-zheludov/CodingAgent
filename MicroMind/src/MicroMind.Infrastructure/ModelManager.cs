using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MicroMind.Core.Abstractions;
using MicroMind.Core.Configuration;
using MicroMind.Core.Exceptions;
using MicroMind.Core.Models;

namespace MicroMind.Infrastructure;

public class ModelManager : IModelManager
{
    private readonly IDownloadProvider _downloadProvider;
    private readonly IOptions<MicroMindOptions> _options;
    private readonly ILogger<ModelManager> _logger;
    private readonly SemaphoreSlim _downloadLock = new(1, 1);
    private string? _cachedModelPath;

    public ModelManager(
        IDownloadProvider downloadProvider,
        IOptions<MicroMindOptions> options,
        ILogger<ModelManager> logger)
    {
        _downloadProvider = downloadProvider ?? throw new ArgumentNullException(nameof(downloadProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task EnsureModelAvailableAsync(CancellationToken cancellationToken = default)
    {
        await _downloadLock.WaitAsync(cancellationToken);
        try
        {
            var modelPath = GetLocalModelPath();
            
            if (File.Exists(modelPath))
            {
                _logger.LogInformation("Model already exists at {ModelPath}", modelPath);
                
                if (_options.Value.Cache.EnableValidation && !string.IsNullOrEmpty(_options.Value.Model.Checksum))
                {
                    _logger.LogInformation("Validating model checksum...");
                    var isValid = await _downloadProvider.ValidateChecksumAsync(
                        modelPath,
                        _options.Value.Model.Checksum,
                        _options.Value.Model.ChecksumAlgorithm,
                        cancellationToken);

                    if (!isValid)
                    {
                        _logger.LogWarning("Model checksum validation failed. Re-downloading model...");
                        File.Delete(modelPath);
                    }
                    else
                    {
                        _logger.LogInformation("Model checksum validation succeeded");
                        _cachedModelPath = modelPath;
                        return;
                    }
                }
                else
                {
                    _cachedModelPath = modelPath;
                    return;
                }
            }

            var cacheDir = Path.GetDirectoryName(modelPath);
            if (!string.IsNullOrEmpty(cacheDir) && !Directory.Exists(cacheDir))
            {
                Directory.CreateDirectory(cacheDir);
                _logger.LogInformation("Created cache directory at {CacheDir}", cacheDir);
            }

            _logger.LogInformation("Downloading model from {SourceUrl} to {ModelPath}", 
                _options.Value.Model.SourceUrl, modelPath);

            var progress = new Progress<DownloadProgress>(p =>
            {
                if (p.PercentComplete.HasValue)
                {
                    _logger.LogInformation("Download progress: {Percent:F2}% ({Downloaded}/{Total} bytes)",
                        p.PercentComplete.Value,
                        p.BytesDownloaded,
                        p.TotalBytes);
                }
            });

            await _downloadProvider.DownloadAsync(
                _options.Value.Model.SourceUrl,
                modelPath,
                progress,
                cancellationToken);

            _logger.LogInformation("Model download completed successfully");

            if (!string.IsNullOrEmpty(_options.Value.Model.Checksum))
            {
                _logger.LogInformation("Validating downloaded model checksum...");
                var isValid = await _downloadProvider.ValidateChecksumAsync(
                    modelPath,
                    _options.Value.Model.Checksum,
                    _options.Value.Model.ChecksumAlgorithm,
                    cancellationToken);

                if (!isValid)
                {
                    File.Delete(modelPath);
                    throw new ModelValidationException(
                        $"Downloaded model checksum validation failed. Expected: {_options.Value.Model.Checksum}");
                }

                _logger.LogInformation("Downloaded model checksum validation succeeded");
            }

            _cachedModelPath = modelPath;
        }
        finally
        {
            _downloadLock.Release();
        }
    }

    public async Task<string> GetModelPathAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedModelPath != null && File.Exists(_cachedModelPath))
        {
            return _cachedModelPath;
        }

        await EnsureModelAvailableAsync(cancellationToken);
        return _cachedModelPath ?? throw new InvalidOperationException("Model path is not available after ensuring model availability");
    }

    public Task InvalidateCacheAsync(CancellationToken cancellationToken = default)
    {
        var modelPath = GetLocalModelPath();
        if (File.Exists(modelPath))
        {
            _logger.LogInformation("Invalidating cached model at {ModelPath}", modelPath);
            File.Delete(modelPath);
            _cachedModelPath = null;
        }

        return Task.CompletedTask;
    }

    public ModelMetadata GetMetadata()
    {
        return new ModelMetadata
        {
            Name = _options.Value.Model.Name,
            Version = _options.Value.Model.Version,
            SizeInBytes = 0, // Will be populated after download
            Capabilities = new ModelCapabilities
            {
                SupportsStreaming = true,
                SupportsChat = true,
                MaxContextLength = _options.Value.Inference.ContextSize,
                MaxOutputLength = _options.Value.Inference.MaxTokens
            }
        };
    }

    private string GetLocalModelPath()
    {
        var cachePath = _options.Value.Cache.CachePath ?? CacheConfiguration.GetDefaultCachePath();
        var fileName = Path.GetFileName(new Uri(_options.Value.Model.SourceUrl).LocalPath);
        return Path.Combine(cachePath, fileName);
    }
}
