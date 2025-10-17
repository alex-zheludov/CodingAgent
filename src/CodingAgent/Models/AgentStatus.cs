namespace CodingAgent.Models;

public enum AgentState
{
    Initializing,
    Idle,
    Working,
    NeedsClarification,
    Complete,
    Error
}

public class AgentStatus
{
    public AgentState State { get; set; }
    public string CurrentActivity { get; set; } = string.Empty;
    public List<string> ThinkingLog { get; set; } = new();
    public RepositoryStatus[] RepositoryStatuses { get; set; } = Array.Empty<RepositoryStatus>();
    public DateTimeOffset LastUpdateTime { get; set; } = DateTimeOffset.UtcNow;
    public TimeSpan ExecutionTime { get; set; }
}

public class RepositoryStatus
{
    public string Name { get; set; } = string.Empty;
    public string CurrentBranch { get; set; } = string.Empty;
    public int ModifiedFiles { get; set; }
    public int StagedFiles { get; set; }
    public int UntrackedFiles { get; set; }
}
