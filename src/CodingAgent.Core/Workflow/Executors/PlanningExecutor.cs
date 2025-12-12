using System.Text.Json;
using Azure;
using Azure.AI.OpenAI;
using CodingAgent.Configuration;
using CodingAgent.Models.Orchestration;
using CodingAgent.Plugins;
using CodingAgent.Services;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace CodingAgent.Core.Workflow.Executors;

/// <summary>
/// Executor that creates execution plans for task requests.
/// Has access to all tools for analysis during planning.
/// </summary>
public sealed class PlanningExecutor : Executor<IntentClassificationResult, PlanningResult>
{
    private readonly AIAgent _agent;
    private readonly ILogger<PlanningExecutor> _logger;

    public PlanningExecutor(
        ModelSettings modelSettings,
        FileOpsPlugin fileOps,
        CodeNavPlugin codeNav,
        GitPlugin git,
        CommandPlugin command,
        ILogger<PlanningExecutor> logger)
        : base("PlanningExecutor")
    {
        _logger = logger;
        _agent = CreateAgent(modelSettings, fileOps, codeNav, git, command);
    }

    public override async ValueTask<PlanningResult> HandleAsync(
        IntentClassificationResult message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var contextInfo = BuildContextInfo(message.WorkspaceContext);

        var prompt = $$"""
            Create an execution plan for this task:
            {{message.OriginalInstruction}}

            Context:
            {{contextInfo}}

            Respond ONLY with valid JSON:
            {
              "task": "task description",
              "steps": [
                {"stepId": 1, "action": "action name", "description": "detailed description", "tools": ["Tool.Method"], "targetFiles": ["path/to/file"], "dependencies": [], "expectedOutcome": "outcome"}
              ],
              "confidence": 0.9
            }
            """;

        var response = await _agent.RunAsync(prompt, cancellationToken: cancellationToken);
        var content = StripMarkdownFences(response.Text ?? "");

        ExecutionPlan plan;
        try
        {
            plan = JsonSerializer.Deserialize<ExecutionPlan>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? CreateFallbackPlan(message.OriginalInstruction);
        }
        catch
        {
            plan = CreateFallbackPlan(message.OriginalInstruction);
        }

        _logger.LogInformation("Created plan with {StepCount} steps", plan.Steps.Count);

        return new PlanningResult
        {
            Plan = plan,
            OriginalTask = message.OriginalInstruction,
            WorkspaceContext = message.WorkspaceContext
        };
    }

    private static AIAgent CreateAgent(
        ModelSettings modelSettings,
        FileOpsPlugin fileOps,
        CodeNavPlugin codeNav,
        GitPlugin git,
        CommandPlugin command)
    {
        var client = new AzureOpenAIClient(
            new Uri(modelSettings.Endpoint),
            new AzureKeyCredential(modelSettings.ApiKey));

        var chatClient = client.GetChatClient(modelSettings.Planning.Model).AsIChatClient();

        // Planning agent gets all tools for analysis
        var tools = new List<AITool>
        {
            // FileOps - all
            AIFunctionFactory.Create(fileOps.ReadFileAsync),
            AIFunctionFactory.Create(fileOps.WriteFileAsync),
            AIFunctionFactory.Create(fileOps.ListDirectoryAsync),
            AIFunctionFactory.Create(fileOps.FindFilesAsync),

            // CodeNav
            AIFunctionFactory.Create(codeNav.GetWorkspaceOverviewAsync),
            AIFunctionFactory.Create(codeNav.GetDirectoryTreeAsync),
            AIFunctionFactory.Create(codeNav.SearchCodeAsync),
            AIFunctionFactory.Create(codeNav.FindDefinitionsAsync),

            // Git - all
            AIFunctionFactory.Create(git.GetStatusAsync),
            AIFunctionFactory.Create(git.GetDiffAsync),
            AIFunctionFactory.Create(git.GetCommitHistoryAsync),
            AIFunctionFactory.Create(git.StageFilesAsync),
            AIFunctionFactory.Create(git.CommitAsync),
            AIFunctionFactory.Create(git.CreateBranchAsync),
            AIFunctionFactory.Create(git.PushAsync),

            // Command
            AIFunctionFactory.Create(command.ExecuteCommandAsync),
            AIFunctionFactory.Create(command.BuildDotnetAsync),
            AIFunctionFactory.Create(command.TestDotnetAsync),
            AIFunctionFactory.Create(command.NpmInstallAsync),
            AIFunctionFactory.Create(command.NpmTestAsync),
        };

        return chatClient.CreateAIAgent(
            instructions: "You are a planning agent that creates execution plans for coding tasks.",
            tools: tools);
    }

    private static ExecutionPlan CreateFallbackPlan(string task)
    {
        return new ExecutionPlan
        {
            Task = task,
            Steps = new List<PlanStep>
            {
                new() { StepId = 1, Action = "Execute task", Description = task, ExpectedOutcome = "Task completed" }
            },
            Confidence = 0.3
        };
    }

    private static string BuildContextInfo(WorkspaceContext? workspaceContext)
    {
        if (workspaceContext == null) return "No workspace context available.";

        var lines = new List<string> { "Available Repositories:" };
        foreach (var (repoName, repoInfo) in workspaceContext.Repositories)
        {
            lines.Add($"  - {repoName}: {repoInfo.TotalFiles} files");
            if (repoInfo.KeyFiles.Count > 0)
            {
                lines.Add($"    Key files: {string.Join(", ", repoInfo.KeyFiles.Take(5))}");
            }
        }
        return string.Join("\n", lines);
    }

    private static string StripMarkdownFences(string content)
    {
        var result = content.Trim();
        if (result.StartsWith("```"))
        {
            var firstLineEnd = result.IndexOf('\n');
            if (firstLineEnd > 0)
                result = result[(firstLineEnd + 1)..];

            var lastFenceIndex = result.LastIndexOf("```");
            if (lastFenceIndex > 0)
                result = result[..lastFenceIndex];

            result = result.Trim();
        }
        return result;
    }
}
