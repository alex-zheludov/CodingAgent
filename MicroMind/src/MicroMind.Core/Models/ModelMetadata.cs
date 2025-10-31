namespace MicroMind.Core.Models;

public class ModelMetadata
{
    public required string Name { get; init; }

    public required string Version { get; init; }

    public long SizeInBytes { get; init; }

    public ModelCapabilities Capabilities { get; init; } = new();

    public Dictionary<string, string> AdditionalProperties { get; init; } = new();
}
