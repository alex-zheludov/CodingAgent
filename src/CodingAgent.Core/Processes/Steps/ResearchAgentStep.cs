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
    public async Task<ResearchResult> AnswerAsync(KernelProcessStepContext context, ResearchInput input)
    {
        _logger.LogInformation("ResearchAgent invoked with question: {Question}", input.Question);

        _kernel ??= _kernelFactory.CreateKernel(AgentCapability.Research);
        _chatService ??= _kernel.GetRequiredService<IChatCompletionService>();

        var contextInfo = BuildContextInfo(input.WorkspaceContext);

        var systemPrompt = $"""
            You are a code research agent. Your job is to answer questions about the codebase by conducting thorough research using available tools.

            ## Your Environment
            {contextInfo}

            ## IMPORTANT: File Path Structure
            - All file paths are relative to the workspace root
            - Each repository is a subdirectory in the workspace
            - To access files in a repository, use paths like: "RepositoryName/path/to/file"
            - Examples:
              - List directory: FileOps-ListDirectory with path "Test-Repo" and pattern "*"
              - Read a file: FileOps-ReadFile with path "Test-Repo/HelloWorld/Program.cs"
              - List subdirectory: FileOps-ListDirectory with path "Test-Repo/HelloWorld" and pattern "*.cs"

            ## Your Capabilities
            You have access to these tools via function calling:

            **FileOps**: Read, write, list, and search files
            **Git**: Check status, create commits, push changes, manage branches
            **CodeNav**: Navigate code structure, find definitions, search patterns

            ## CRITICAL: How to Answer Questions

            **Be proactive and investigative!**
            - ALWAYS use tools to gather information before answering
            - For questions like "what does this project do?" or "tell me about repository":
              1. Check the workspace context to see which repositories exist
              2. Try FileOps-ReadFile("RepositoryName/README.md") for each repository
              3. List repository contents: FileOps-ListDirectory("RepositoryName", "*")
              4. Read key files found in the workspace context
              5. Examine actual code to understand purpose
            - **NEVER give up after one failed tool call**
            - **NEVER say "I can't determine" without trying multiple approaches**
            - Provide comprehensive information based on actual code examination
            - End your response with <DONE>

            ## Examples

            User: "what's in the Test-Repo repository?"
            You: [calls FileOps-ListDirectory("Test-Repo", "*")]
            You: "The Test-Repo repository contains: HelloWorld/ folder and project files. <DONE>"

            User: "what does this repository do?"
            You: [sees Test-Repo in workspace context]
            You: [calls FileOps-ReadFile("Test-Repo/README.md")] - file not found
            You: [calls FileOps-ListDirectory("Test-Repo", "*")] - sees HelloWorld folder
            You: [calls FileOps-ReadFile("Test-Repo/HelloWorld/HelloWorld.csproj")] - examines project
            You: [calls FileOps-ReadFile("Test-Repo/HelloWorld/Program.cs")] - reads entry point
            You: "This repository contains a simple .NET console application called HelloWorld that prints 'Hello World' to the console. <DONE>"

            IMPORTANT: If you don't call any tools in your response, you MUST include <DONE> marker.
            """;

        var chatHistory = new ChatHistory();
        chatHistory.AddSystemMessage(systemPrompt);
        chatHistory.AddUserMessage($"Question: {input.Question}");

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

            var toolCallsDetected = response.Metadata?.ContainsKey("ToolCalls") == true;
            _logger.LogInformation("Research iteration {Iteration}: ToolCalls={ToolCalls}, ContentLength={Length}",
                iteration, toolCallsDetected, content.Length);

            if (content.Contains("<DONE>", StringComparison.Ordinal))
            {
                finalAnswer = content.Replace("<DONE>", "").Trim();
                _logger.LogInformation("Research completed with <DONE> marker. Answer length: {Length}", finalAnswer.Length);
                break;
            }

            if (!toolCallsDetected && iteration > 0)
            {
                finalAnswer = content;
                _logger.LogInformation("Research completed without tools. Answer length: {Length}", finalAnswer.Length);
                break;
            }
        }

        if (string.IsNullOrEmpty(finalAnswer))
        {
            _logger.LogWarning("Research completed {MaxIterations} iterations without producing an answer", maxIterations);
        }

        var result = new ResearchResult
        {
            Answer = finalAnswer,
            References = new List<CodeReference>(),
            Confidence = 0.85
        };

        // Create SummaryResearchInput for the summary step
        var summaryInput = new SummaryResearchInput
        {
            Research = result,
            OriginalQuestion = input.Question
        };

        await context.EmitEventAsync(new KernelProcessEvent { Id = OutputEvents.AnswerReady, Data = summaryInput });

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
