using CodingAgent.Core.Models.Orchestration;
using CodingAgent.Core.Services;

namespace CodingAgent.Core.Workflow;

/// <summary>
/// Input message for the workflow containing user instruction and workspace context.
/// </summary>
public sealed class WorkflowInput
{
    public string Instruction { get; set; } = string.Empty;
    public WorkspaceContext WorkspaceContext { get; set; } = new();
}

/// <summary>
/// Result from intent classification with routing information.
/// </summary>
public sealed class IntentClassificationResult
{
    public IntentType Intent { get; set; }
    public double Confidence { get; set; }
    public string Reasoning { get; set; } = string.Empty;
    public string OriginalInstruction { get; set; } = string.Empty;
    public WorkspaceContext WorkspaceContext { get; set; } = new();
}

public sealed class ContextDiscoveryResult
{
    public string OriginalInstruction { get; set; } = string.Empty;
    public WorkspaceContext WorkspaceContext { get; set; } = new();
}

/// <summary>
/// Result from the planning phase.
/// </summary>
public sealed class PlanningResult
{
    public ExecutionPlan Plan { get; set; } = new();
    public string OriginalTask { get; set; } = string.Empty;
    public WorkspaceContext WorkspaceContext { get; set; } = new();
}
