namespace CodingAgent.Core.Configuration;

public class AzureOpenAISettings
{
    public const string SectionName = "AzureOpenAI";

    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int MaxTokens { get; set; } = 4096;
    public decimal Temperature { get; set; } = 0.3m;
}
