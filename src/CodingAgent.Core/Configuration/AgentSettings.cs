namespace CodingAgent.Configuration;

public class AgentSettings
{
    public const string SectionName = "Agent";


    public int MaxExecutionMinutes { get; set; } = 30;
    public bool AutoCommit { get; set; } = true;
    public bool AutoPush { get; set; } = false;
    public List<RepositoryConfig> Repositories { get; set; } = new();
    public WorkspaceConfig Workspace { get; set; } = new();
    public SessionConfig Session { get; set; } = new();
}

public class RepositoryConfig
{
    public string Name { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string? LocalPath { get; set; }
    public string Branch { get; set; } = string.Empty;
}

public class SessionConfig
{
    public string SessionId { get; set; } = string.Empty;
    public string Root { get; set; } = string.Empty;
}

public class WorkspaceConfig
{
    public string Root { get; set; } = string.Empty;
}
