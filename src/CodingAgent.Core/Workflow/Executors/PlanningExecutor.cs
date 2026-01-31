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
    private readonly ModelSettings _modelSettings;
    private readonly ILogger<PlanningExecutor> _logger;
    private readonly IFileOperationsPlugin _fileOperations;
    private readonly ICodeNavigationPlugin _codeNavigation;
    private readonly IGitPlugin _git;
    private readonly ICommandPlugin _command;

    public PlanningExecutor(
        ModelSettings modelSettings,
        ILogger<PlanningExecutor> logger,
        IFileOperationsPlugin fileOperations,
        ICodeNavigationPlugin codeNavigation,
        IGitPlugin git,
        ICommandPlugin command,
        ExecutorOptions? options = null,
        bool declareCrossRunShareable = false)
        : base(nameof(PlanningExecutor), options, declareCrossRunShareable)
    {
        _modelSettings = modelSettings;
        _logger = logger;
        _fileOperations = fileOperations;
        _codeNavigation = codeNavigation;
        _git = git;
        _command = command;
        _agent = CreateAgent();
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

    private AIAgent CreateAgent()
    {
        var client = new AzureOpenAIClient(
            new Uri(_modelSettings.Endpoint),
            new AzureKeyCredential(_modelSettings.ApiKey));

        var chatClient = client.GetChatClient(_modelSettings.Planning.Model).AsIChatClient();

        // Planning agent gets all tools for analysis
        var tools = new List<AITool>
        {
            // File Operations - all
            AIFunctionFactory.Create(_fileOperations.ReadFileAsync),
            AIFunctionFactory.Create(_fileOperations.WriteFileAsync),
            AIFunctionFactory.Create(_fileOperations.ListDirectoryAsync),
            AIFunctionFactory.Create(_fileOperations.FindFilesAsync),

            // Code Navigation
            AIFunctionFactory.Create(_codeNavigation.GetWorkspaceOverviewAsync),
            AIFunctionFactory.Create(_codeNavigation.GetDirectoryTreeAsync),
            AIFunctionFactory.Create(_codeNavigation.SearchCodeAsync),
            AIFunctionFactory.Create(_codeNavigation.FindDefinitionsAsync),

            // Git - all
            AIFunctionFactory.Create(_git.GetStatusAsync),
            AIFunctionFactory.Create(_git.GetDiffAsync),
            AIFunctionFactory.Create(_git.GetCommitHistoryAsync),
            AIFunctionFactory.Create(_git.StageFilesAsync),
            AIFunctionFactory.Create(_git.CommitAsync),
            AIFunctionFactory.Create(_git.CreateBranchAsync),
            AIFunctionFactory.Create(_git.PushAsync),

            // Command
            AIFunctionFactory.Create(_command.ExecuteCommandAsync),
            AIFunctionFactory.Create(_command.BuildDotnetAsync),
            AIFunctionFactory.Create(_command.TestDotnetAsync),
            AIFunctionFactory.Create(_command.NpmInstallAsync),
            AIFunctionFactory.Create(_command.NpmTestAsync),
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
