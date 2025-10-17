namespace CodingAgent.Configuration;

public class OrchestratorSettings
{
    public const string SectionName = "Orchestrator";

    public string? HubUrl { get; set; }
    public string? ApiKey { get; set; }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(HubUrl);
}
