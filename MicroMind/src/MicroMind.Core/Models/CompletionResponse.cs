namespace MicroMind.Core.Models;

public class CompletionResponse
{
    public required string Text { get; init; }

    public int PromptTokens { get; init; }

    public int CompletionTokens { get; init; }

    public int TotalTokens => PromptTokens + CompletionTokens;

    public FinishReason FinishReason { get; init; } = FinishReason.Stop;

    public Dictionary<string, object> Metadata { get; init; } = new();
}

public enum FinishReason
{
    Stop,

    Length,

    Cancelled,

    Error
}
