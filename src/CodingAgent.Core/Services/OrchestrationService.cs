#pragma warning disable SKEXP0080 // SK Process Framework is experimental

using CodingAgent.Models;
using CodingAgent.Models.Orchestration;
using CodingAgent.Processes;
using CodingAgent.Processes.Steps;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Process;

namespace CodingAgent.Services;

public interface IOrchestrationService
{
    Task<SummaryResult> ProcessInstructionAsync(string instruction);
    Task<AgentState> GetStatusAsync();
}

public class OrchestrationService : IOrchestrationService
{
    private readonly IKernelFactory _kernelFactory;
    private readonly IWorkspaceManager _workspaceManager;
    private readonly ILogger<OrchestrationService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private OrchestrationState _state;

    public OrchestrationService(
        IKernelFactory kernelFactory,
        IWorkspaceManager workspaceManager,
        ILogger<OrchestrationService> logger,
        IServiceProvider serviceProvider)
    {
        _kernelFactory = kernelFactory;
        _workspaceManager = workspaceManager;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _state = new OrchestrationState();
    }

    public async Task<SummaryResult> ProcessInstructionAsync(string instruction)
    {
        try
        {
            _state.OriginalInput = instruction;
            _state.Status = AgentState.Working;

            // Get workspace context
            var workspaceContext = await _workspaceManager.ScanWorkspaceAsync();

            // Build the process
            var process = CodingOrchestrationProcess.BuildProcess();

            // Create kernel for process execution
            var kernel = _kernelFactory.CreateKernel(AgentCapability.IntentClassification);

            // Start the process with Start event
            // Note: The process will run asynchronously and emit events between steps
            // The final Summary step will be triggered automatically by the process framework
            await process.StartAsync(
                kernel,
                new KernelProcessEvent
                {
                    Id = "Start",
                    Data = new { input = instruction, workspaceContext }
                });

            _state.Status = AgentState.Complete;

            // For now, return a placeholder summary
            // TODO: In a real implementation, we'd need to capture the summary result from the process
            // This might require updating the Summary step to store its result somewhere accessible
            var summaryResult = _state.SummaryResult ?? new SummaryResult
            {
                Summary = "Process completed successfully",
                Metrics = new SummaryMetrics
                {
                    StepsCompleted = 0,
                    StepsTotal = 0,
                    SuccessRate = "100%"
                }
            };

            return summaryResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing instruction");
            _state.Status = AgentState.Error;

            return new SummaryResult
            {
                Summary = $"Error: {ex.Message}",
                Metrics = new SummaryMetrics()
            };
        }
    }

    public Task<AgentState> GetStatusAsync()
    {
        return Task.FromResult(_state.Status);
    }
}
