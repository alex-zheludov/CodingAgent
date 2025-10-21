#pragma warning disable SKEXP0080 // SK Process Framework is experimental

using CodingAgent.Models.Orchestration;
using CodingAgent.Services;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Process;

namespace CodingAgent.Processes.Steps;

public class ExecutionAgentStep : KernelProcessStep
{
    public static class Functions
    {
        public const string ExecutePlan = nameof(ExecutePlan);
    }

    public static class OutputEvents
    {
        public const string PlanCompleted = nameof(PlanCompleted);
    }

    private readonly IKernelFactory _kernelFactory;
    private readonly ILogger<ExecutionAgentStep> _logger;
    private Kernel? _kernel;
    private IChatCompletionService? _chatService;

    public ExecutionAgentStep(
        IKernelFactory kernelFactory,
        ILogger<ExecutionAgentStep> logger)
    {
        _kernelFactory = kernelFactory;
        _logger = logger;
    }

    [KernelFunction(Functions.ExecutePlan)]
    public async Task<List<StepResult>> ExecutePlanAsync(KernelProcessStepContext context, ExecutionPlan plan, WorkspaceContext workspaceContext)
    {
        _kernel ??= _kernelFactory.CreateKernel(AgentCapability.Execution);
        _chatService ??= _kernel.GetRequiredService<IChatCompletionService>();

        var results = new List<StepResult>();

        foreach (var step in plan.Steps.OrderBy(s => s.StepId))
        {
            var dependenciesMet = step.Dependencies.All(depId =>
                results.Any(r => r.StepId == depId && r.Status == StepStatus.Completed));

            if (!dependenciesMet)
            {
                results.Add(new StepResult
                {
                    StepId = step.StepId,
                    Status = StepStatus.Skipped,
                    Outcome = "Dependencies not met"
                });
                continue;
            }

            _logger.LogInformation("Executing step {StepId}: {Action}", step.StepId, step.Action);

            var startTime = DateTime.UtcNow;
            var result = await ExecuteStepAsync(step);
            result.ExecutionTime = DateTime.UtcNow - startTime;

            results.Add(result);

            if (result.Status == StepStatus.Failed && result.Error?.Recoverable == false)
            {
                break;
            }
        }

        await context.EmitEventAsync(new KernelProcessEvent { Id = OutputEvents.PlanCompleted, Data = results });

        return results;
    }

    private async Task<StepResult> ExecuteStepAsync(PlanStep step)
    {
        var systemPrompt = $"""
            Execute this step:
            {step.Description}

            Expected outcome: {step.ExpectedOutcome}
            Tools available: {string.Join(", ", step.Tools)}

            Execute the step and end with <STEP COMPLETE>
            """;

        var chatHistory = new ChatHistory();
        chatHistory.AddSystemMessage(systemPrompt);
        chatHistory.AddUserMessage($"Execute: {step.Action}");

        var executionSettings = new OpenAIPromptExecutionSettings
        {
            MaxTokens = 4096,
            Temperature = 0.3,
            ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
        };

        const int maxIterations = 10;
        var outcome = string.Empty;

        try
        {
            for (int iteration = 0; iteration < maxIterations; iteration++)
            {
                var response = await _chatService!.GetChatMessageContentAsync(
                    chatHistory,
                    executionSettings: executionSettings,
                    kernel: _kernel);

                var content = response.Content ?? "";
                chatHistory.AddAssistantMessage(content);

                if (content.Contains("<STEP COMPLETE>", StringComparison.Ordinal))
                {
                    outcome = content.Replace("<STEP COMPLETE>", "").Trim();
                    break;
                }

                var toolCallsDetected = response.Metadata?.ContainsKey("ToolCalls") == true;
                if (!toolCallsDetected && iteration > 0)
                {
                    outcome = content;
                    break;
                }
            }

            return new StepResult
            {
                StepId = step.StepId,
                Status = StepStatus.Completed,
                Outcome = outcome.Length > 500 ? outcome.Substring(0, 500) : outcome,
                Confidence = 0.85
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing step {StepId}", step.StepId);

            return new StepResult
            {
                StepId = step.StepId,
                Status = StepStatus.Failed,
                Outcome = "Execution failed",
                Error = new StepError
                {
                    Type = ex.GetType().Name,
                    Message = ex.Message,
                    Recoverable = false
                }
            };
        }
    }
}
