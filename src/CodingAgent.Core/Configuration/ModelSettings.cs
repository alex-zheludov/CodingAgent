namespace CodingAgent.Core.Configuration;

public class ModelSettings
{
    public const string SectionName = "Models";

    // Shared Azure OpenAI Configuration
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;

    // Agent-specific configurations
    public AgentModelConfig IntentClassifier { get; set; } = new();
    public AgentModelConfig Research { get; set; } = new();
    public AgentModelConfig Planning { get; set; } = new();
    public AgentModelConfig Summary { get; set; } = new();
    public AgentModelConfig Execution { get; set; } = new();
}

public class AgentModelConfig
{
    // Model deployment name
    public string Model { get; set; } = string.Empty;

    // Generation Parameters
    public int MaxTokens { get; set; }
    public decimal Temperature { get; set; }

    // Optional: Response format for structured output
    public string ResponseFormat { get; set; } = "text"; // "text" | "json"
}
