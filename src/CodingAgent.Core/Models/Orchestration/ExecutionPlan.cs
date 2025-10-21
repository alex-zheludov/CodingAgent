namespace CodingAgent.Models.Orchestration;

public class ExecutionPlan
{
    public string PlanId { get; set; } = Guid.NewGuid().ToString();
    public string Task { get; set; } = string.Empty;
    public int EstimatedIterations { get; set; }
    public string EstimatedDuration { get; set; } = string.Empty;
    public List<PlanStep> Steps { get; set; } = new();
    public List<PlanRisk> Risks { get; set; } = new();
    public List<string> RequiredTools { get; set; } = new();
    public double Confidence { get; set; }
}

public class PlanStep
{
    public int StepId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> Tools { get; set; } = new();
    public List<string> TargetFiles { get; set; } = new();
    public List<int> Dependencies { get; set; } = new();
    public string ExpectedOutcome { get; set; } = string.Empty;
}

public class PlanRisk
{
    public string Description { get; set; } = string.Empty;
    public string Mitigation { get; set; } = string.Empty;
    public string Severity { get; set; } = "low"; // "low" | "medium" | "high"
}
