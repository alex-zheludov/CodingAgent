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
        FileOperationsPlugin fileOperations,
        CodeNavigationPlugin codeNavigation,
        GitPlugin git,
        ILogger<ResearchExecutor> logger)
        : base(nameof(ResearchExecutor))
    {
        _settings = agentSettings;
        _logger = logger;
        _agent = CreateAgent(modelSettings, fileOperations, codeNavigation, git);
    }

    public override async ValueTask<SummaryResult> HandleAsync(
        IntentClassificationResult message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var prompt = $$"""
            You are a code research agent. Answer questions about the codebase using available tools.

            {{message.WorkspaceContext.DiscoveredContext}}

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
        FileOperationsPlugin fileOperations,
        CodeNavigationPlugin codeNavigation,
        GitPlugin git)
    {
        var client = new AzureOpenAIClient(
            new Uri(modelSettings.Endpoint),
            new AzureKeyCredential(modelSettings.ApiKey));

        var chatClient = client.GetChatClient(modelSettings.Research.Model).AsIChatClient();

        // Research agent gets read-only tools
        var tools = new List<AITool>
        {
            // File Operations - read only
            AIFunctionFactory.Create(fileOperations.ReadFileAsync),
            AIFunctionFactory.Create(fileOperations.ListDirectoryAsync),
            AIFunctionFactory.Create(fileOperations.FindFilesAsync),

            // CodeNavivagtion
            AIFunctionFactory.Create(codeNavigation.GetWorkspaceOverviewAsync),
            AIFunctionFactory.Create(codeNavigation.GetDirectoryTreeAsync),
            AIFunctionFactory.Create(codeNavigation.SearchCodeAsync),
            AIFunctionFactory.Create(codeNavigation.FindDefinitionsAsync),

            // Git - read only
            AIFunctionFactory.Create(git.GetStatusAsync),
            AIFunctionFactory.Create(git.GetDiffAsync),
            AIFunctionFactory.Create(git.GetCommitHistoryAsync),
        };

        return chatClient.CreateAIAgent(
            instructions: "You are a code research agent. Use available tools to answer questions about the codebase.",
            tools: tools);
    }
}
