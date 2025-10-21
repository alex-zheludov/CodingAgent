#pragma warning disable SKEXP0080 // SK Process Framework is experimental

using CodingAgent.Models.Orchestration;
using CodingAgent.Services;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Process;
using System.Text.Json;

namespace CodingAgent.Processes.Steps;

public class PlanningAgentStep : KernelProcessStep
{
    public static class Functions
    {
        public const string CreatePlan = nameof(CreatePlan);
    }

    public static class OutputEvents
    {
        public const string PlanReady = nameof(PlanReady);
    }

    private readonly IKernelFactory _kernelFactory;
    private readonly ILogger<PlanningAgentStep> _logger;
    private Kernel? _kernel;
    private IChatCompletionService? _chatService;

    public PlanningAgentStep(
        IKernelFactory kernelFactory,
        ILogger<PlanningAgentStep> logger)
    {
        _kernelFactory = kernelFactory;
        _logger = logger;
    }

    [KernelFunction(Functions.CreatePlan)]
    public async Task<ExecutionPlan> CreatePlanAsync(KernelProcessStepContext context, string task, WorkspaceContext workspaceContext)
    {
        _kernel ??= _kernelFactory.CreateKernel(AgentCapability.Planning);
        _chatService ??= _kernel.GetRequiredService<IChatCompletionService>();

        var contextInfo = BuildContextInfo(workspaceContext);

        var systemPrompt = $$"""
            You are a planning agent using DeepSeek R1. Create detailed execution plans.

            ## Environment
            {{contextInfo}}

            ## Response Format (JSON only):
            {
              "planId": "plan-uuid",
              "task": "task description",
              "estimatedIterations": 5,
              "estimatedDuration": "5-10 minutes",
              "steps": [
                {
                  "stepId": 1,
                  "action": "action name",
                  "description": "detailed description",
                  "tools": ["FileOps.ReadFile"],
                  "targetFiles": ["path/to/file"],
                  "dependencies": [],
                  "expectedOutcome": "outcome"
                }
              ],
              "risks": [],
              "requiredTools": ["FileOps"],
              "confidence": 0.9
            }
            """;

        var chatHistory = new ChatHistory();
        chatHistory.AddSystemMessage(systemPrompt);
        chatHistory.AddUserMessage($"Create a plan for: {task}");

        var response = await _chatService.GetChatMessageContentAsync(chatHistory, kernel: _kernel);
        var content = response.Content ?? throw new InvalidOperationException("No response");

        try
        {
            var plan = JsonSerializer.Deserialize<ExecutionPlan>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? throw new InvalidOperationException("Failed to deserialize");

            _logger.LogInformation("Created plan with {StepCount} steps", plan.Steps.Count);

            await context.EmitEventAsync(new KernelProcessEvent { Id = OutputEvents.PlanReady, Data = plan });

            return plan;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse plan");

            var fallbackPlan = new ExecutionPlan
            {
                Task = task,
                EstimatedIterations = 5,
                Steps = new List<PlanStep>
                {
                    new PlanStep
                    {
                        StepId = 1,
                        Action = "Execute task",
                        Description = task,
                        ExpectedOutcome = "Task completed"
                    }
                },
                Confidence = 0.3
            };

            await context.EmitEventAsync(new KernelProcessEvent { Id = OutputEvents.PlanReady, Data = fallbackPlan });

            return fallbackPlan;
        }
    }

    private string BuildContextInfo(WorkspaceContext workspaceContext)
    {
        var lines = new List<string> { "Workspace:" };
        foreach (var (repoName, repoInfo) in workspaceContext.Repositories)
        {
            lines.Add($"  {repoName}: {repoInfo.TotalFiles} files");
        }
        return string.Join("\n", lines);
    }
}
