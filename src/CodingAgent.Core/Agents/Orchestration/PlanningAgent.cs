using CodingAgent.Models.Orchestration;
using CodingAgent.Services;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Text.Json;

namespace CodingAgent.Agents.Orchestration;

public interface IPlanningAgent
{
    Task<ExecutionPlan> CreatePlanAsync(string task, WorkspaceContext workspaceContext);
}

public class PlanningAgent : IPlanningAgent
{
    private readonly Kernel _kernel;
    private readonly IChatCompletionService _chatService;
    private readonly ILogger<PlanningAgent> _logger;

    public PlanningAgent(
        IKernelFactory kernelFactory,
        ILogger<PlanningAgent> logger)
    {
        _kernel = kernelFactory.CreateKernel(AgentCapability.Planning);
        _chatService = _kernel.GetRequiredService<IChatCompletionService>();
        _logger = logger;
    }

    public async Task<ExecutionPlan> CreatePlanAsync(string task, WorkspaceContext workspaceContext)
    {
        var contextInfo = BuildContextInfo(workspaceContext);

        var systemPrompt = $$"""
            You are a planning agent using DeepSeek R1 for deep reasoning. Create detailed execution plans for coding tasks.

            ## Your Environment
            {{contextInfo}}

            ## Available Tools
            - FileOps: ReadFile, WriteFile, ListDirectory, SearchFiles, DeleteFile
            - Git: Status, Commit, Push, Pull, CreateBranch
            - Command: DotnetBuild, DotnetTest, NpmInstall, NpmTest
            - CodeNav: SearchCode, FindDefinition

            ## Your Task
            Decompose the task into sequential, executable steps. Use your deep reasoning capabilities to:
            1. Analyze the task thoroughly
            2. Identify file dependencies
            3. Determine the correct order of operations
            4. Consider edge cases and risks
            5. Create a comprehensive plan

            ## Response Format
            Respond with ONLY valid JSON in this exact format:
            {
              "planId": "plan-uuid",
              "task": "task description",
              "estimatedIterations": 5,
              "estimatedDuration": "5-10 minutes",
              "steps": [
                {
                  "stepId": 1,
                  "action": "brief action name",
                  "description": "detailed description",
                  "tools": ["FileOps.ReadFile"],
                  "targetFiles": ["path/to/file.cs"],
                  "dependencies": [],
                  "expectedOutcome": "what should happen"
                }
              ],
              "risks": [
                {
                  "description": "potential risk",
                  "mitigation": "how to handle it",
                  "severity": "low|medium|high"
                }
              ],
              "requiredTools": ["FileOps", "Git"],
              "confidence": 0.9
            }

            IMPORTANT:
            - Maximum 15 steps
            - Dependencies refer to stepIds
            - All file paths must be valid workspace paths
            - Be specific about which tool functions to use
            - Do NOT include any text before or after the JSON
            """;

        var userPrompt = $"Create a plan for this task: {task}";

        var chatHistory = new ChatHistory();
        chatHistory.AddSystemMessage(systemPrompt);
        chatHistory.AddUserMessage(userPrompt);

        var response = await _chatService.GetChatMessageContentAsync(chatHistory, kernel: _kernel);
        var content = response.Content ?? throw new InvalidOperationException("No response from planning agent");

        _logger.LogInformation("Planning Agent Response: {Response}", content);

        // Parse JSON response
        try
        {
            var plan = JsonSerializer.Deserialize<ExecutionPlan>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (plan == null)
                throw new InvalidOperationException("Failed to deserialize execution plan");

            _logger.LogInformation(
                "Created plan with {StepCount} steps (Estimated: {Duration})",
                plan.Steps.Count,
                plan.EstimatedDuration);

            return plan;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse planning response as JSON");

            // Return a simple fallback plan
            return new ExecutionPlan
            {
                Task = task,
                EstimatedIterations = 5,
                EstimatedDuration = "Unknown - planning failed",
                Steps = new List<PlanStep>
                {
                    new PlanStep
                    {
                        StepId = 1,
                        Action = "Execute task",
                        Description = $"Execute: {task}",
                        Tools = new List<string> { "FileOps", "Git", "Command" },
                        ExpectedOutcome = "Task completed"
                    }
                },
                Confidence = 0.3
            };
        }
    }

    private string BuildContextInfo(WorkspaceContext workspaceContext)
    {
        var lines = new List<string>();
        lines.Add("Workspace Context:");

        foreach (var (repoName, repoInfo) in workspaceContext.Repositories)
        {
            lines.Add($"  Repository: {repoName}");
            lines.Add($"    - Path: {repoInfo.Path}");
            lines.Add($"    - Total Files: {repoInfo.TotalFiles}");

            if (repoInfo.FilesByExtension.Any())
            {
                lines.Add("    - File Types: " + string.Join(", ",
                    repoInfo.FilesByExtension.OrderByDescending(x => x.Value).Take(5)
                    .Select(x => $"{x.Key} ({x.Value})")));
            }
        }

        return string.Join("\n", lines);
    }
}
