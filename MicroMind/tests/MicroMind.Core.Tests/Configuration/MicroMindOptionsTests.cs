using MicroMind.Core.Configuration;
using Shouldly;

namespace MicroMind.Core.Tests.Configuration;

public class MicroMindOptionsTests
{
    [Fact]
    public void MicroMindOptions_DefaultValues_ShouldBeValid()
    {
        var options = new MicroMindOptions();

        options.Model.ShouldNotBeNull();
        options.Inference.ShouldNotBeNull();
        options.Download.ShouldNotBeNull();
        options.Cache.ShouldNotBeNull();
    }

    [Fact]
    public void ModelConfiguration_DefaultValues_ShouldBeValid()
    {
        var config = new ModelConfiguration();

        config.Name.ShouldBe("Phi-3-mini-4k-instruct");
        config.SourceUrl.ShouldContain("huggingface.co");
        config.ChecksumAlgorithm.ShouldBe("SHA256");
        config.Version.ShouldBe("1.0.0");
    }

    [Fact]
    public void InferenceConfiguration_DefaultValues_ShouldBeValid()
    {
        var config = new InferenceConfiguration();

        config.Temperature.ShouldBe(0.7f);
        config.MaxTokens.ShouldBe(2048);
        config.TopP.ShouldBe(0.95f);
        config.Runtime.ShouldBe("LLamaSharp");
        config.EagerLoading.ShouldBeFalse();
        config.GpuLayers.ShouldBe(0);
        config.ContextSize.ShouldBe(4096);
    }

    [Fact]
    public void DownloadConfiguration_DefaultValues_ShouldBeValid()
    {
        var config = new DownloadConfiguration();

        config.MaxRetries.ShouldBe(3);
        config.RetryDelayMs.ShouldBe(1000);
        config.TimeoutSeconds.ShouldBe(600);
        config.UseExponentialBackoff.ShouldBeTrue();
    }

    [Fact]
    public void CacheConfiguration_GetDefaultCachePath_ShouldReturnValidPath()
    {
        var path = CacheConfiguration.GetDefaultCachePath();

        path.ShouldNotBeNullOrEmpty();
        path.ShouldContain("MicroMind");
        path.ShouldContain("models");
    }
}
