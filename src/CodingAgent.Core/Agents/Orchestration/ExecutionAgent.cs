using CodingAgent.Models.Orchestration;
using CodingAgent.Services;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace CodingAgent.Agents.Orchestration;

public interface IExecutionAgent
{
    Task<List<StepResult>> ExecutePlanAsync(ExecutionPlan plan, WorkspaceContext workspaceContext);
}

public class ExecutionAgent : IExecutionAgent
{
    private readonly Kernel _kernel;
    private readonly IChatCompletionService _chatService;
    private readonly ILogger<ExecutionAgent> _logger;

    public ExecutionAgent(
        IKernelFactory kernelFactory,
        ILogger<ExecutionAgent> logger)
    {
        _kernel = kernelFactory.CreateKernel(AgentCapability.Execution);
        _chatService = _kernel.GetRequiredService<IChatCompletionService>();
        _logger = logger;
    }

    public async Task<List<StepResult>> ExecutePlanAsync(ExecutionPlan plan, WorkspaceContext workspaceContext)
    {
        var results = new List<StepResult>();
        var contextInfo = BuildContextInfo(workspaceContext);

        foreach (var step in plan.Steps.OrderBy(s => s.StepId))
        {
            // Check dependencies
            var dependenciesMet = step.Dependencies.All(depId =>
                results.Any(r => r.StepId == depId && r.Status == StepStatus.Completed));

            if (!dependenciesMet)
            {
                _logger.LogWarning("Step {StepId} skipped: dependencies not met", step.StepId);
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
            var result = await ExecuteStepAsync(step, results, contextInfo);
            result.ExecutionTime = DateTime.UtcNow - startTime;

            results.Add(result);

            // If step failed and not recoverable, stop execution
            if (result.Status == StepStatus.Failed && result.Error?.Recoverable == false)
            {
                _logger.LogError("Step {StepId} failed with non-recoverable error. Stopping execution.", step.StepId);
                break;
            }
        }

        return results;
    }

    private async Task<StepResult> ExecuteStepAsync(
        PlanStep step,
        List<StepResult> previousResults,
        string contextInfo)
    {
        var systemPrompt = $"""
            You are an execution agent. Execute this specific step of a plan.

            ## Your Environment
            {contextInfo}

            ## Current Step
            Step {step.StepId}: {step.Action}
            Description: {step.Description}
            Expected Outcome: {step.ExpectedOutcome}

            ## Available Tools
            You have access to: {string.Join(", ", step.Tools)}

            ## Previous Step Results
            {BuildPreviousResultsSummary(previousResults, step.Dependencies)}

            ## Instructions
            1. Execute ONLY this specific step
            2. Use only the tools specified for this step
            3. Work towards the expected outcome
            4. When done, describe what you accomplished
            5. End with <STEP COMPLETE> when finished

            Focus on completing this one step precisely as described.
            """;

        var chatHistory = new ChatHistory();
        chatHistory.AddSystemMessage(systemPrompt);
        chatHistory.AddUserMessage($"Execute step: {step.Action}");

        var executionSettings = new OpenAIPromptExecutionSettings
        {
            MaxTokens = 4096,
            Temperature = 0.3,
            ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
        };

        const int maxIterations = 10;
        var iteration = 0;
        var outcome = string.Empty;
        var filesModified = new List<string>();
        var toolsUsed = new List<ToolInvocation>();

        try
        {
            while (iteration < maxIterations)
            {
                iteration++;
                _logger.LogDebug("Step {StepId} iteration {Iteration}", step.StepId, iteration);

                var response = await _chatService.GetChatMessageContentAsync(
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

                // If no tools called and not first iteration, we're done
                var toolCallsDetected = response.Metadata?.ContainsKey("ToolCalls") == true;
                if (!toolCallsDetected && iteration > 1)
                {
                    outcome = content;
                    break;
                }
            }

            return new StepResult
            {
                StepId = step.StepId,
                Status = StepStatus.Completed,
                Outcome = outcome.Length > 500 ? outcome.Substring(0, 500) + "..." : outcome,
                FilesModified = filesModified,
                ToolsUsed = toolsUsed,
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
                    Recoverable = ex is not InvalidOperationException
                }
            };
        }
    }

    private string BuildContextInfo(WorkspaceContext workspaceContext)
    {
        var lines = new List<string>();
        lines.Add("Workspace Context:");

        foreach (var (repoName, repoInfo) in workspaceContext.Repositories)
        {
            lines.Add($"  Repository: {repoName} ({repoInfo.Path})");
        }

        return string.Join("\n", lines);
    }

    private string BuildPreviousResultsSummary(List<StepResult> results, List<int> dependencies)
    {
        if (!dependencies.Any())
            return "No dependencies - this is an independent step.";

        var relevantResults = results.Where(r => dependencies.Contains(r.StepId)).ToList();

        return string.Join("\n", relevantResults.Select(r =>
            $"Step {r.StepId}: {r.Outcome} (Status: {r.Status})"));
    }
}
