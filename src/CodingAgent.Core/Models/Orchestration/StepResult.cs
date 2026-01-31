namespace CodingAgent.Core.Models.Orchestration;

public class StepResult
{
    public int StepId { get; set; }
    public StepStatus Status { get; set; }
    public TimeSpan ExecutionTime { get; set; }
    public List<ToolInvocation> ToolsUsed { get; set; } = new();
    public string Outcome { get; set; } = string.Empty;
    public List<string> FilesModified { get; set; } = new();
    public string NextStepRecommendation { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public StepError? Error { get; set; }
}

public enum StepStatus
{
    Pending,
    InProgress,
    Completed,
    Failed,
    Skipped
}

public class ToolInvocation
{
    public string Tool { get; set; } = string.Empty;
    public Dictionary<string, object> Parameters { get; set; } = new();
    public string Result { get; set; } = string.Empty;
    public TimeSpan ExecutionTime { get; set; }
}

public class StepError
{
    public string Type { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string ToolInvolved { get; set; } = string.Empty;
    public bool Recoverable { get; set; }
}
