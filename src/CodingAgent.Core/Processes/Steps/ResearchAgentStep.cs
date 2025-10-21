#pragma warning disable SKEXP0080 // SK Process Framework is experimental

using CodingAgent.Models.Orchestration;
using CodingAgent.Services;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Process;

namespace CodingAgent.Processes.Steps;

public class ResearchAgentStep : KernelProcessStep
{
    public static class Functions
    {
        public const string Answer = nameof(Answer);
    }

    public static class OutputEvents
    {
        public const string AnswerReady = nameof(AnswerReady);
    }

    private readonly IKernelFactory _kernelFactory;
    private readonly ILogger<ResearchAgentStep> _logger;
    private Kernel? _kernel;
    private IChatCompletionService? _chatService;

    public ResearchAgentStep(
        IKernelFactory kernelFactory,
        ILogger<ResearchAgentStep> logger)
    {
        _kernelFactory = kernelFactory;
        _logger = logger;
    }

    [KernelFunction(Functions.Answer)]
    public async Task<ResearchResult> AnswerAsync(KernelProcessStepContext context, string question, WorkspaceContext workspaceContext)
    {
        _kernel ??= _kernelFactory.CreateKernel(AgentCapability.Research);
        _chatService ??= _kernel.GetRequiredService<IChatCompletionService>();

        var contextInfo = BuildContextInfo(workspaceContext);

        var systemPrompt = $"""
            You are a code research agent. Answer questions about the codebase using available tools.

            ## Your Environment
            {contextInfo}

            ## Your Capabilities
            Use FileOps, Git, and CodeNav tools to gather information.
            Provide accurate answers with file/line references.
            End your response with <DONE> when finished.
            """;

        var chatHistory = new ChatHistory();
        chatHistory.AddSystemMessage(systemPrompt);
        chatHistory.AddUserMessage($"Question: {question}");

        var executionSettings = new OpenAIPromptExecutionSettings
        {
            MaxTokens = 16384,
            Temperature = 0.3,
            ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
        };

        const int maxIterations = 10;
        var finalAnswer = string.Empty;

        for (int iteration = 0; iteration < maxIterations; iteration++)
        {
            var response = await _chatService.GetChatMessageContentAsync(
                chatHistory,
                executionSettings: executionSettings,
                kernel: _kernel);

            var content = response.Content ?? "";
            chatHistory.AddAssistantMessage(content);

            if (content.Contains("<DONE>", StringComparison.Ordinal))
            {
                finalAnswer = content.Replace("<DONE>", "").Trim();
                break;
            }

            var toolCallsDetected = response.Metadata?.ContainsKey("ToolCalls") == true;
            if (!toolCallsDetected && iteration > 0)
            {
                finalAnswer = content;
                break;
            }
        }

        var result = new ResearchResult
        {
            Answer = finalAnswer,
            References = new List<CodeReference>(),
            Confidence = 0.85
        };

        await context.EmitEventAsync(new KernelProcessEvent { Id = OutputEvents.AnswerReady, Data = result });

        return result;
    }

    private string BuildContextInfo(WorkspaceContext workspaceContext)
    {
        var lines = new List<string> { "Workspace Context:" };
        foreach (var (repoName, repoInfo) in workspaceContext.Repositories)
        {
            lines.Add($"  Repository: {repoName} - {repoInfo.TotalFiles} files");
        }
        return string.Join("\n", lines);
    }
}
