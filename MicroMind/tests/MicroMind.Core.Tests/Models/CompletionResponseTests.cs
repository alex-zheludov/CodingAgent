using MicroMind.Core.Models;
using Shouldly;

namespace MicroMind.Core.Tests.Models;

public class CompletionResponseTests
{
    [Fact]
    public void CompletionResponse_TotalTokens_ShouldCalculateCorrectly()
    {
        var response = new CompletionResponse
        {
            Text = "Test response",
            PromptTokens = 10,
            CompletionTokens = 20
        };

        response.TotalTokens.ShouldBe(30);
    }

    [Fact]
    public void CompletionResponse_WithMetadata_ShouldStoreCorrectly()
    {
        var metadata = new Dictionary<string, object>
        {
            { "model", "test-model" },
            { "version", "1.0" }
        };

        var response = new CompletionResponse
        {
            Text = "Test",
            PromptTokens = 5,
            CompletionTokens = 10,
            Metadata = metadata
        };

        response.Metadata.Count.ShouldBe(2);
        response.Metadata["model"].ShouldBe("test-model");
        response.Metadata["version"].ShouldBe("1.0");
    }

    [Fact]
    public void CompletionResponse_DefaultFinishReason_ShouldBeStop()
    {
        var response = new CompletionResponse
        {
            Text = "Test",
            PromptTokens = 5,
            CompletionTokens = 10
        };

        response.FinishReason.ShouldBe(FinishReason.Stop);
    }
}
