using System.Text.Json;
using Azure;
using Azure.AI.OpenAI;
using CodingAgent.Core.Configuration;
using CodingAgent.Core.Models.Orchestration;
using CodingAgent.Core.Plugins;
using CodingAgent.Core.Services;
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
        FileOperationsPlugin fileOperations,
        CodeNavigationPlugin codeNavigation,
        GitPlugin git,
        CommandPlugin command,
        ILogger<PlanningExecutor> logger)
        : base(nameof(PlanningExecutor))
    {
        _logger = logger;
        _agent = CreateAgent(modelSettings, fileOperations, codeNavigation, git, command);
    }

    public override async ValueTask<PlanningResult> HandleAsync(
        IntentClassificationResult message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var prompt = $$"""
            Create an execution plan for this task:
            {{message.OriginalInstruction}}

            Context:
            {{message.WorkspaceContext.DiscoveredContext}}

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
        FileOperationsPlugin fileOperations,
        CodeNavigationPlugin codeNavigation,
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
            // File Operations - all
            AIFunctionFactory.Create(fileOperations.ReadFileAsync),
            AIFunctionFactory.Create(fileOperations.WriteFileAsync),
            AIFunctionFactory.Create(fileOperations.ListDirectoryAsync),
            AIFunctionFactory.Create(fileOperations.FindFilesAsync),

            // Code Navigation
            AIFunctionFactory.Create(codeNavigation.GetWorkspaceOverviewAsync),
            AIFunctionFactory.Create(codeNavigation.GetDirectoryTreeAsync),
            AIFunctionFactory.Create(codeNavigation.SearchCodeAsync),
            AIFunctionFactory.Create(codeNavigation.FindDefinitionsAsync),

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
