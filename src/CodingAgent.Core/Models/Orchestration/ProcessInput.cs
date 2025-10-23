using CodingAgent.Services;

namespace CodingAgent.Models.Orchestration;

/// <summary>
/// Input for the orchestration process
/// </summary>
public class ProcessInput
{
    /// <summary>
    /// The user's input/instruction
    /// </summary>
    public string Input { get; set; } = string.Empty;

    /// <summary>
    /// The workspace context
    /// </summary>
    public WorkspaceContext WorkspaceContext { get; set; } = null!;
}

/// <summary>
/// Input for research step
/// </summary>
public class ResearchInput
{
    public string Question { get; set; } = string.Empty;
    public WorkspaceContext WorkspaceContext { get; set; } = null!;
    public IntentResult? Intent { get; set; }
}

/// <summary>
/// Input for planning step
/// </summary>
public class PlanningInput
{
    public string Task { get; set; } = string.Empty;
    public WorkspaceContext WorkspaceContext { get; set; } = null!;
    public IntentResult? Intent { get; set; }
}

/// <summary>
/// Input for execution step
/// </summary>
public class ExecutionInput
{
    public ExecutionPlan Plan { get; set; } = null!;
    public WorkspaceContext WorkspaceContext { get; set; } = null!;
}

/// <summary>
/// Input for summary step (research)
/// </summary>
public class SummaryResearchInput
{
    public ResearchResult Research { get; set; } = null!;
    public string OriginalQuestion { get; set; } = string.Empty;
}

/// <summary>
/// Input for summary step (task)
/// </summary>
public class SummaryTaskInput
{
    public List<StepResult> Steps { get; set; } = new();
    public string OriginalTask { get; set; } = string.Empty;
    public ExecutionPlan Plan { get; set; } = null!;
}
