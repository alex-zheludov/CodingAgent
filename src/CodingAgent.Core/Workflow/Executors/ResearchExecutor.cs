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
/// Executor that performs research/question answering using read-only code analysis tools.
/// </summary>
public sealed class ResearchExecutor : Executor<IntentClassificationResult, SummaryResult>
{
    private readonly AIAgent _agent;
    private readonly AgentSettings _settings;
    private readonly ILogger<ResearchExecutor> _logger;

    public ResearchExecutor(
        ModelSettings modelSettings,
        AgentSettings agentSettings,
        FileOpsPlugin fileOps,
        CodeNavPlugin codeNav,
        GitPlugin git,
        ILogger<ResearchExecutor> logger)
        : base("ResearchExecutor")
    {
        _settings = agentSettings;
        _logger = logger;
        _agent = CreateAgent(modelSettings, fileOps, codeNav, git);
    }

    public override async ValueTask<SummaryResult> HandleAsync(
        IntentClassificationResult message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var contextInfo = BuildContextInfo(message.WorkspaceContext);
        
        var prompt = $$"""
            You are a code research agent. Answer questions about the codebase using available tools.

            {{contextInfo}}

            Use tools to:
            - Read files with ReadFileAsync
            - List directories with ListDirectoryAsync
            - Search code with SearchCodeAsync
            - Find definitions with FindDefinitionsAsync

            Provide a comprehensive answer. End with <DONE> when finished.

            Question: {{message.OriginalInstruction}}
            """;

        var thread = _agent.GetNewThread();
        string finalAnswer = "";

        for (int i = 0; i < _settings.MaxIterationsPerStep; i++)
        {
            var userInput = i == 0 ? prompt : "Continue your research and answer. End with <DONE> when finished.";
            
            var response = await _agent.RunAsync(userInput, thread, cancellationToken: cancellationToken);
            var content = response.Text ?? "";

            if (content.Contains("<DONE>"))
            {
                finalAnswer = content.Replace("<DONE>", "").Trim();
                break;
            }

            if (!string.IsNullOrEmpty(content) && i > 0)
            {
                finalAnswer = content;
                break;
            }
        }

        _logger.LogInformation("Research completed for question");

        return new SummaryResult
        {
            Summary = "Research completed",
            KeyFindings = new List<string> { finalAnswer },
            Metrics = new SummaryMetrics { StepsCompleted = 1, StepsTotal = 1, SuccessRate = "100%" }
        };
    }

    private static AIAgent CreateAgent(
        ModelSettings modelSettings,
        FileOpsPlugin fileOps,
        CodeNavPlugin codeNav,
        GitPlugin git)
    {
        var client = new AzureOpenAIClient(
            new Uri(modelSettings.Endpoint),
            new AzureKeyCredential(modelSettings.ApiKey));

        var chatClient = client.GetChatClient(modelSettings.Research.Model).AsIChatClient();

        // Research agent gets read-only tools
        var tools = new List<AITool>
        {
            // FileOps - read only
            AIFunctionFactory.Create(fileOps.ReadFileAsync),
            AIFunctionFactory.Create(fileOps.ListDirectoryAsync),
            AIFunctionFactory.Create(fileOps.FindFilesAsync),

            // CodeNav
            AIFunctionFactory.Create(codeNav.GetWorkspaceOverviewAsync),
            AIFunctionFactory.Create(codeNav.GetDirectoryTreeAsync),
            AIFunctionFactory.Create(codeNav.SearchCodeAsync),
            AIFunctionFactory.Create(codeNav.FindDefinitionsAsync),

            // Git - read only
            AIFunctionFactory.Create(git.GetStatusAsync),
            AIFunctionFactory.Create(git.GetDiffAsync),
            AIFunctionFactory.Create(git.GetCommitHistoryAsync),
        };

        return chatClient.CreateAIAgent(
            instructions: "You are a code research agent. Use available tools to answer questions about the codebase.",
            tools: tools);
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
}
