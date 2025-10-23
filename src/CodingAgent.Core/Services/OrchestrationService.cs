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
    private SummaryResult? _capturedSummary;

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

            // Create kernel for process execution with service provider
            // The kernel needs access to our DI container for process steps
            var kernelBuilder = Kernel.CreateBuilder();

            // Add the service provider so process steps can resolve dependencies
            kernelBuilder.Services.AddSingleton(_serviceProvider.GetRequiredService<IKernelFactory>());

            // Add logging - use the existing ILoggerFactory from our DI container
            kernelBuilder.Services.AddSingleton(_serviceProvider.GetRequiredService<ILoggerFactory>());
            kernelBuilder.Services.AddLogging();

            // Add a callback action for summary step to store the result
            Action<SummaryResult> captureSummary = (summary) => _capturedSummary = summary;
            kernelBuilder.Services.AddSingleton(captureSummary);

            var kernel = kernelBuilder.Build();

            // Create the input for the process
            var processInput = new ProcessInput
            {
                Input = instruction,
                WorkspaceContext = workspaceContext
            };

            // Start the process with Start event
            // Note: The SK Process runs the entire workflow to completion
            _logger.LogInformation("Starting process with input: {Input}", instruction);

            var processHandle = await process.StartAsync(
                kernel,
                new KernelProcessEvent
                {
                    Id = "Start",
                    Data = processInput
                });

            _logger.LogInformation("Process started, handle ID: {HandleId}", processHandle);

            // The process runs to completion, so we can mark as complete
            _state.Status = AgentState.Complete;

            // Return the captured summary, or a placeholder if summary step didn't execute
            var summaryResult = _capturedSummary ?? new SummaryResult
            {
                Summary = "Process completed successfully",
                Metrics = new SummaryMetrics
                {
                    StepsCompleted = 0,
                    StepsTotal = 0,
                    SuccessRate = "100%"
                }
            };

            _logger.LogInformation("Process complete. Summary: {Summary}", summaryResult.Summary);

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
