namespace MicroMind.Core.Models;

public class CompletionRequest
{
    public string? SystemPrompt { get; init; }

    public required string UserPrompt { get; init; }

    public List<ConversationMessage> History { get; init; } = new();

    public float Temperature { get; init; } = 0.7f;

    public int MaxTokens { get; init; } = 2048;

    public float TopP { get; init; } = 0.95f;

    public int? TopK { get; init; }

    public float RepetitionPenalty { get; init; } = 1.0f;
}
