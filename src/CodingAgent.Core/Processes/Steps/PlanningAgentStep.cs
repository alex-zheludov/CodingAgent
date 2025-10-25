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
    public async Task<ExecutionPlan> CreatePlanAsync(KernelProcessStepContext context, EnrichedPlanningInput input)
    {
        _kernel ??= _kernelFactory.CreateKernel(AgentCapability.Planning);
        _chatService ??= _kernel.GetRequiredService<IChatCompletionService>();

        // Use pre-built context summary from ContextAgent
        var contextInfo = string.Join("\n", input.EnrichedContexts.Values
            .Select(ec => ec.PlanningContextSummary));

        var systemPrompt = $$"""
            You are a planning agent using DeepSeek R1. Create detailed execution plans.

            ## Environment
            {{contextInfo}}

            ## File Path Rules
            IMPORTANT: All file paths in targetFiles must be relative to the workspace root and MUST include the repository name.
            Format: RepositoryName/path/to/file
            Example: "Test-Repo/HelloWorld/Program.cs" NOT "HelloWorld/Program.cs"

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
                  "targetFiles": ["RepositoryName/path/to/file"],
                  "dependencies": [],
                  "expectedOutcome": "outcome"
                }
              ],
              "risks": [
                {
                  "description": "risk description",
                  "mitigation": "mitigation strategy",
                  "severity": "low"
                }
              ],
              "requiredTools": ["FileOps"],
              "confidence": 0.9
            }
            """;

        var chatHistory = new ChatHistory();
        chatHistory.AddSystemMessage(systemPrompt);
        chatHistory.AddUserMessage($"Create a plan for: {input.Task}");

        var response = await _chatService.GetChatMessageContentAsync(chatHistory, kernel: _kernel);
        var content = response.Content ?? throw new InvalidOperationException("No response");

        _logger.LogInformation("Planning response length: {Length}, Content preview: {Preview}",
            content.Length, content.Length > 100 ? content.Substring(0, 100) : content);

        // Strip markdown code fences if present (```json ... ``` or ``` ... ```)
        var jsonContent = content.Trim();
        if (jsonContent.StartsWith("```"))
        {
            // Remove opening fence (```json or ```)
            var firstLineEnd = jsonContent.IndexOf('\n');
            if (firstLineEnd > 0)
            {
                jsonContent = jsonContent.Substring(firstLineEnd + 1);
            }

            // Remove closing fence (```)
            var lastFenceIndex = jsonContent.LastIndexOf("```");
            if (lastFenceIndex > 0)
            {
                jsonContent = jsonContent.Substring(0, lastFenceIndex);
            }

            jsonContent = jsonContent.Trim();
            _logger.LogInformation("Stripped markdown fences. New length: {Length}", jsonContent.Length);
        }

        try
        {
            var plan = JsonSerializer.Deserialize<ExecutionPlan>(jsonContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? throw new InvalidOperationException("Failed to deserialize");

            _logger.LogInformation("Created plan with {StepCount} steps", plan.Steps.Count);

            // Print the plan details
            _logger.LogInformation("=== EXECUTION PLAN ===");
            _logger.LogInformation("Plan ID: {PlanId}", plan.PlanId);
            _logger.LogInformation("Task: {Task}", plan.Task);
            _logger.LogInformation("Estimated Duration: {Duration}", plan.EstimatedDuration);
            _logger.LogInformation("Estimated Iterations: {Iterations}", plan.EstimatedIterations);
            _logger.LogInformation("Confidence: {Confidence:P0}", plan.Confidence);

            _logger.LogInformation("--- Steps ---");
            foreach (var step in plan.Steps)
            {
                _logger.LogInformation("  Step {StepId}: {Action}", step.StepId, step.Action);
                _logger.LogInformation("    Description: {Description}", step.Description);
                _logger.LogInformation("    Tools: {Tools}", string.Join(", ", step.Tools));
                _logger.LogInformation("    Target Files: {Files}", string.Join(", ", step.TargetFiles));
                _logger.LogInformation("    Expected Outcome: {Outcome}", step.ExpectedOutcome);
                if (step.Dependencies.Any())
                {
                    _logger.LogInformation("    Dependencies: {Dependencies}", string.Join(", ", step.Dependencies));
                }
            }

            if (plan.Risks.Any())
            {
                _logger.LogInformation("--- Risks ---");
                foreach (var risk in plan.Risks)
                {
                    _logger.LogInformation("  - {Description} (Severity: {Severity})", risk.Description, risk.Severity);
                    _logger.LogInformation("    Mitigation: {Mitigation}", risk.Mitigation);
                }
            }

            _logger.LogInformation("=====================");

            // Create ExecutionInput for the next step
            var executionInput = new EnrichedExecutionInput
            {
                Plan = plan,
                WorkspaceContext = input.WorkspaceContext,
                EnrichedContexts = input.EnrichedContexts
            };

            await context.EmitEventAsync(new KernelProcessEvent { Id = OutputEvents.PlanReady, Data = executionInput });

            return plan;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize plan JSON. Using fallback. Response was: {Response}",
                content.Length > 500 ? content.Substring(0, 500) : content);

            var fallbackPlan = new ExecutionPlan
            {
                Task = input.Task,
                EstimatedIterations = 5,
                Steps = new List<PlanStep>
                {
                    new PlanStep
                    {
                        StepId = 1,
                        Action = "Execute task",
                        Description = input.Task,
                        ExpectedOutcome = "Task completed"
                    }
                },
                Confidence = 0.3
            };

            // Create ExecutionInput for the fallback plan
            var fallbackExecutionInput = new EnrichedExecutionInput
            {
                Plan = fallbackPlan,
                WorkspaceContext = input.WorkspaceContext,
                EnrichedContexts = input.EnrichedContexts
            };

            await context.EmitEventAsync(new KernelProcessEvent { Id = OutputEvents.PlanReady, Data = fallbackExecutionInput });

            return fallbackPlan;
        }
    }
}
