namespace MicroMind.Core.Models;

public class CompletionChunk
{
    public required string Text { get; init; }

    public bool IsFinal { get; init; }

    public FinishReason? FinishReason { get; init; }
}
