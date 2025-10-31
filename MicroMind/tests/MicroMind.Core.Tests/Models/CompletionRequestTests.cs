using MicroMind.Core.Models;
using Shouldly;

namespace MicroMind.Core.Tests.Models;

public class CompletionRequestTests
{
    [Fact]
    public void CompletionRequest_WithValidData_ShouldCreateSuccessfully()
    {
        var request = new CompletionRequest
        {
            UserPrompt = "Test prompt",
            SystemPrompt = "System instructions",
            Temperature = 0.8f,
            MaxTokens = 1024,
            TopP = 0.9f
        };

        request.UserPrompt.ShouldBe("Test prompt");
        request.SystemPrompt.ShouldBe("System instructions");
        request.Temperature.ShouldBe(0.8f);
        request.MaxTokens.ShouldBe(1024);
        request.TopP.ShouldBe(0.9f);
        request.History.ShouldNotBeNull();
        request.History.ShouldBeEmpty();
    }

    [Fact]
    public void CompletionRequest_WithHistory_ShouldMaintainMessages()
    {
        var history = new List<ConversationMessage>
        {
            new() { Role = MessageRole.User, Content = "Hello" },
            new() { Role = MessageRole.Assistant, Content = "Hi there!" }
        };

        var request = new CompletionRequest
        {
            UserPrompt = "How are you?",
            History = history
        };

        request.History.Count.ShouldBe(2);
        request.History[0].Role.ShouldBe(MessageRole.User);
        request.History[0].Content.ShouldBe("Hello");
        request.History[1].Role.ShouldBe(MessageRole.Assistant);
        request.History[1].Content.ShouldBe("Hi there!");
    }

    [Fact]
    public void CompletionRequest_DefaultValues_ShouldBeValid()
    {
        var request = new CompletionRequest
        {
            UserPrompt = "Test"
        };

        request.Temperature.ShouldBe(0.7f);
        request.MaxTokens.ShouldBe(2048);
        request.TopP.ShouldBe(0.95f);
        request.RepetitionPenalty.ShouldBe(1.0f);
    }
}
