using CodingAgent.Services;

namespace CodingAgent.Models.Orchestration;

public class OrchestrationState
{
    // Input
    public string OriginalInput { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public WorkspaceContext? WorkspaceContext { get; set; }

    // Intent Classification
    public IntentType? Intent { get; set; }
    public double IntentConfidence { get; set; }

    // Planning
    public ExecutionPlan? Plan { get; set; }
    public int CurrentStepIndex { get; set; }

    // Execution
    public List<StepResult> StepResults { get; set; } = new();
    public List<string> ModifiedFiles { get; set; } = new();
    public List<ToolInvocation> ToolInvocations { get; set; } = new();

    // Research
    public ResearchResult? ResearchResult { get; set; }

    // Summary
    public SummaryResult? SummaryResult { get; set; }

    // Output
    public string FinalResponse { get; set; } = string.Empty;
    public AgentState Status { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }

    // Metrics
    public Dictionary<string, object> Metrics { get; set; } = new()
    {
        ["IntentClassificationTime"] = 0.0,
        ["ResearchTime"] = 0.0,
        ["PlanningTime"] = 0.0,
        ["ExecutionTime"] = 0.0,
        ["SummaryTime"] = 0.0,
        ["TotalTokensUsed"] = 0,
        ["TotalCost"] = 0.0
    };
}
