using System.Text.Json;
using Azure;
using Azure.AI.OpenAI;
using CodingAgent.Core.Configuration;
using CodingAgent.Core.Plugins;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace CodingAgent.Core.Workflow.Executors;

public class ContextDiscoveryExecutor : Executor<WorkflowInput, ContextDiscoveryResult>
{
    private readonly AIAgent _agent;
    private readonly ModelSettings _modelSettings;
    private readonly ILogger<ContextDiscoveryExecutor> _logger;
    private readonly IFileOperationsPlugin _fileOperations;
    private readonly ICodeNavigationPlugin _codeNavigation;
    private readonly IGitPlugin _git;

    public ContextDiscoveryExecutor
    (
        ModelSettings modelSettings,
        ILogger<ContextDiscoveryExecutor> logger,
        IFileOperationsPlugin fileOperations,
        ICodeNavigationPlugin codeNavigation,
        IGitPlugin git,
        ExecutorOptions? options = null,
        bool declareCrossRunShareable = false
    ) : base(nameof(ContextDiscoveryExecutor), options, declareCrossRunShareable)
    {
        _modelSettings = modelSettings;
        _logger = logger;
        _fileOperations = fileOperations;
        _codeNavigation = codeNavigation;
        _git = git;
        _agent = CreateAgent();
    }

    public override async ValueTask<ContextDiscoveryResult> HandleAsync
        (WorkflowInput message, IWorkflowContext context, CancellationToken cancellationToken = new CancellationToken())
    {
        // TODO: Make context discovery more generic, and add caching to bypass agent execution if no changes
        // Pass originally discovered context to the next executor if changes are detected
        // And instruct to review pending changes or git history since last change
        // Track commit sha as a versioning strategy
        
        var serializedContext = JsonSerializer.Serialize(message.WorkspaceContext);
        
        var prompt = $$"""
                       You are a code discovery agent. Discover the context for the user's request.'
                       Request: {{message.Instruction}}
                       Discovered Context: {{serializedContext}}
                       """;
        
        var response = await _agent.RunAsync(prompt, cancellationToken: cancellationToken);

        message.WorkspaceContext.DiscoveredContext = response.Text;
        
        return new ContextDiscoveryResult()
        {
            OriginalInstruction = message.Instruction,
            WorkspaceContext = message.WorkspaceContext
        };
    }

    private AIAgent CreateAgent()
    {
        var client = new AzureOpenAIClient(
            new Uri(_modelSettings.Endpoint),
            new AzureKeyCredential(_modelSettings.ApiKey));

        var chatClient = client.GetChatClient(_modelSettings.Execution.Model).AsIChatClient();

        // Execution agent gets all tools
        var tools = new List<AITool>
        {
            // File Operations - Read
            AIFunctionFactory.Create(_fileOperations.ReadFileAsync),
            AIFunctionFactory.Create(_fileOperations.ListDirectoryAsync),
            AIFunctionFactory.Create(_fileOperations.FindFilesAsync),

            // Code Navigation
            AIFunctionFactory.Create(_codeNavigation.GetWorkspaceOverviewAsync),
            AIFunctionFactory.Create(_codeNavigation.GetDirectoryTreeAsync),
            AIFunctionFactory.Create(_codeNavigation.SearchCodeAsync),
            AIFunctionFactory.Create(_codeNavigation.FindDefinitionsAsync),

            // Git - Read
            AIFunctionFactory.Create(_git.GetStatusAsync),
            AIFunctionFactory.Create(_git.GetDiffAsync),
            AIFunctionFactory.Create(_git.GetCommitHistoryAsync),
        };

        return chatClient.CreateAIAgent(
            instructions: @"Your job is to discover the context for user's request, determine the type of the project, repository structure, 
                            dependencies, and other relevant information necessery for agents that will run in the future stages of the workflow.",
            tools: tools);
    }
}