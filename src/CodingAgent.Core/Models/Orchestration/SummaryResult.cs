namespace CodingAgent.Core.Models.Orchestration;

public class SummaryResult
{
    public string Summary { get; set; } = string.Empty;
    public List<string> Accomplishments { get; set; } = new();
    public List<string> KeyFindings { get; set; } = new();
    public FileChanges? FilesChanged { get; set; }
    public SummaryMetrics? Metrics { get; set; }
    public List<string> NextSteps { get; set; } = new();
    public List<string> FilesReferenced { get; set; } = new();
    public ExecutionPlan? Plan { get; set; }
}

public class FileChanges
{
    public List<string> Created { get; set; } = new();
    public List<string> Modified { get; set; } = new();
    public List<string> Deleted { get; set; } = new();
}

public class SummaryMetrics
{
    public string ExecutionTime { get; set; } = string.Empty;
    public int StepsCompleted { get; set; }
    public int StepsTotal { get; set; }
    public string SuccessRate { get; set; } = string.Empty;
}
