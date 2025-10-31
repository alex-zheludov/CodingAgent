using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MicroMind.Core.Abstractions;
using MicroMind.Core.Configuration;
using MicroMind.Core.Exceptions;
using MicroMind.Core.Models;
using MicroMind.Infrastructure;
using Shouldly;

namespace MicroMind.Infrastructure.Tests;

public class ModelManagerTests
{
    private readonly Mock<IDownloadProvider> _mockDownloadProvider;
    private readonly Mock<ILogger<ModelManager>> _mockLogger;
    private readonly IOptions<MicroMindOptions> _options;

    public ModelManagerTests()
    {
        _mockDownloadProvider = new Mock<IDownloadProvider>();
        _mockLogger = new Mock<ILogger<ModelManager>>();
        
        _options = Options.Create(new MicroMindOptions
        {
            Model = new ModelConfiguration
            {
                Name = "test-model",
                SourceUrl = "https://example.com/model.gguf",
                Version = "1.0.0"
            },
            Cache = new CacheConfiguration
            {
                CachePath = Path.Combine(Path.GetTempPath(), "micromind-test"),
                EnableValidation = false
            }
        });
    }

    [Fact]
    public void Constructor_WithNullDownloadProvider_ShouldThrowArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => 
            new ModelManager(null!, _options, _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullOptions_ShouldThrowArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => 
            new ModelManager(_mockDownloadProvider.Object, null!, _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => 
            new ModelManager(_mockDownloadProvider.Object, _options, null!));
    }

    [Fact]
    public void GetMetadata_ShouldReturnCorrectMetadata()
    {
        var manager = new ModelManager(_mockDownloadProvider.Object, _options, _mockLogger.Object);

        var metadata = manager.GetMetadata();

        metadata.ShouldNotBeNull();
        metadata.Name.ShouldBe("test-model");
        metadata.Version.ShouldBe("1.0.0");
        metadata.Capabilities.ShouldNotBeNull();
    }

    [Fact]
    public async Task EnsureModelAvailableAsync_WhenModelExists_ShouldNotDownload()
    {
        var tempFile = Path.Combine(_options.Value.Cache.CachePath!, "model.gguf");
        Directory.CreateDirectory(_options.Value.Cache.CachePath!);
        await File.WriteAllTextAsync(tempFile, "test content");

        var manager = new ModelManager(_mockDownloadProvider.Object, _options, _mockLogger.Object);

        try
        {
            await manager.EnsureModelAvailableAsync();

            _mockDownloadProvider.Verify(
                x => x.DownloadAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<IProgress<DownloadProgress>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
        finally
        {
            if (Directory.Exists(_options.Value.Cache.CachePath))
            {
                Directory.Delete(_options.Value.Cache.CachePath, true);
            }
        }
    }

    [Fact]
    public async Task GetModelPathAsync_WhenModelNotAvailable_ShouldEnsureAvailability()
    {
        var tempFile = Path.Combine(_options.Value.Cache.CachePath!, "model.gguf");
        Directory.CreateDirectory(_options.Value.Cache.CachePath!);
        
        _mockDownloadProvider
            .Setup(x => x.DownloadAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IProgress<DownloadProgress>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, IProgress<DownloadProgress>?, CancellationToken>(
                (_, targetPath, _, _) => File.WriteAllText(targetPath, "test"));

        var manager = new ModelManager(_mockDownloadProvider.Object, _options, _mockLogger.Object);

        try
        {
            var path = await manager.GetModelPathAsync();

            path.ShouldNotBeNullOrEmpty();
            File.Exists(path).ShouldBeTrue();
        }
        finally
        {
            if (Directory.Exists(_options.Value.Cache.CachePath))
            {
                Directory.Delete(_options.Value.Cache.CachePath, true);
            }
        }
    }

    [Fact]
    public async Task InvalidateCacheAsync_WhenModelExists_ShouldDeleteModel()
    {
        var tempFile = Path.Combine(_options.Value.Cache.CachePath!, "model.gguf");
        Directory.CreateDirectory(_options.Value.Cache.CachePath!);
        await File.WriteAllTextAsync(tempFile, "test content");

        var manager = new ModelManager(_mockDownloadProvider.Object, _options, _mockLogger.Object);

        try
        {
            await manager.InvalidateCacheAsync();

            File.Exists(tempFile).ShouldBeFalse();
        }
        finally
        {
            if (Directory.Exists(_options.Value.Cache.CachePath))
            {
                Directory.Delete(_options.Value.Cache.CachePath, true);
            }
        }
    }
}
