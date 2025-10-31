namespace MicroMind.Core.Models;

public class ModelCapabilities
{
    public bool SupportsStreaming { get; init; } = true;

    public bool SupportsChat { get; init; } = true;

    public int MaxContextLength { get; init; } = 4096;

    public int MaxOutputLength { get; init; } = 2048;
}
