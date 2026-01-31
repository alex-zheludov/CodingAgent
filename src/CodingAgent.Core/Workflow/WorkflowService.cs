using CodingAgent.Core.Models.Orchestration;
using CodingAgent.Core.Services;
using Microsoft.Agents.AI.Workflows;

namespace CodingAgent.Core.Workflow;

/// <summary>
/// Service that manages workflow execution.
/// </summary>
public interface IWorkflowService
{
    Task<SummaryResult> ProcessInstructionAsync(string instruction);
}

public class WorkflowService : IWorkflowService
{
    private readonly Microsoft.Agents.AI.Workflows.Workflow _workflow;
    private readonly IWorkspaceManager _workspaceManager;
    private readonly ILogger<WorkflowService> _logger;

    public WorkflowService(
        CodingAgentWorkflowBuilder workflowBuilder,
        IWorkspaceManager workspaceManager,
        ILogger<WorkflowService> logger)
    {
        _workflow = workflowBuilder.Build();
        _workspaceManager = workspaceManager;
        _logger = logger;
    }

    public async Task<SummaryResult> ProcessInstructionAsync(string instruction)
    {
        try
        {
            // Get workspace context
            var workspaceContext = await _workspaceManager.ScanWorkspaceAsync();

            // Create workflow input
            var input = new WorkflowInput
            {
                Instruction = instruction,
                WorkspaceContext = workspaceContext
            };

            _logger.LogInformation("Starting workflow for instruction: {Instruction}",
                instruction.Length > 100 ? instruction[..100] + "..." : instruction);

            // Execute workflow
            await using var run = await InProcessExecution.RunAsync(_workflow, input);

            SummaryResult? result = null;

            foreach (var evt in run.NewEvents)
            {
                if (evt is WorkflowOutputEvent outputEvent && outputEvent.Data is SummaryResult summaryResult)
                {
                    result = summaryResult;
                }
                else if (evt is ExecutorCompletedEvent executorComplete)
                {
                    _logger.LogDebug("Executor completed: {ExecutorId}", executorComplete.ExecutorId);
                }
            }

            return result ?? new SummaryResult
            {
                Summary = "Workflow completed but no result was produced.",
                Metrics = new SummaryMetrics()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing instruction");

            return new SummaryResult
            {
                Summary = $"Error: {ex.Message}",
                Metrics = new SummaryMetrics()
            };
        }
    }
}
