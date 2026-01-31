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
    private readonly ModelSettings _modelSettings;
    private readonly AgentSettings _agentSettings;
    private readonly ILogger<ResearchExecutor> _logger;
    private readonly IFileOperationsPlugin _fileOperations;
    private readonly ICodeNavigationPlugin _codeNavigation;
    private readonly IGitPlugin _git;

    public ResearchExecutor(
        ModelSettings modelSettings,
        AgentSettings agentSettings,
        ILogger<ResearchExecutor> logger,
        IFileOperationsPlugin fileOperations,
        ICodeNavigationPlugin codeNavigation,
        IGitPlugin git,
        ExecutorOptions? options = null,
        bool declareCrossRunShareable = false)
        : base(nameof(ResearchExecutor), options, declareCrossRunShareable)
    {
        _modelSettings = modelSettings;
        _agentSettings = agentSettings;
        _logger = logger;
        _fileOperations = fileOperations;
        _codeNavigation = codeNavigation;
        _git = git;
        _agent = CreateAgent();
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

        for (int i = 0; i < _agentSettings.MaxIterationsPerStep; i++)
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

    private AIAgent CreateAgent()
    {
        var client = new AzureOpenAIClient(
            new Uri(_modelSettings.Endpoint),
            new AzureKeyCredential(_modelSettings.ApiKey));

        var chatClient = client.GetChatClient(_modelSettings.Research.Model).AsIChatClient();

        // Research agent gets read-only tools
        var tools = new List<AITool>
        {
            // File Operations - read only
            AIFunctionFactory.Create(_fileOperations.ReadFileAsync),
            AIFunctionFactory.Create(_fileOperations.ListDirectoryAsync),
            AIFunctionFactory.Create(_fileOperations.FindFilesAsync),

            // CodeNavivagtion
            AIFunctionFactory.Create(_codeNavigation.GetWorkspaceOverviewAsync),
            AIFunctionFactory.Create(_codeNavigation.GetDirectoryTreeAsync),
            AIFunctionFactory.Create(_codeNavigation.SearchCodeAsync),
            AIFunctionFactory.Create(_codeNavigation.FindDefinitionsAsync),

            // Git - read only
            AIFunctionFactory.Create(_git.GetStatusAsync),
            AIFunctionFactory.Create(_git.GetDiffAsync),
            AIFunctionFactory.Create(_git.GetCommitHistoryAsync),
        };

        return chatClient.CreateAIAgent(
            instructions: "You are a code research agent. Use available tools to answer questions about the codebase.",
            tools: tools);
    }
}
