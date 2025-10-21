using CodingAgent.Models.Orchestration;
using CodingAgent.Services;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace CodingAgent.Agents.Orchestration;

public interface IResearchAgent
{
    Task<ResearchResult> AnswerAsync(string question, WorkspaceContext workspaceContext);
}

public class ResearchAgent : IResearchAgent
{
    private readonly Kernel _kernel;
    private readonly IChatCompletionService _chatService;
    private readonly ILogger<ResearchAgent> _logger;

    public ResearchAgent(
        IKernelFactory kernelFactory,
        ILogger<ResearchAgent> logger)
    {
        _kernel = kernelFactory.CreateKernel(AgentCapability.Research);
        _chatService = _kernel.GetRequiredService<IChatCompletionService>();
        _logger = logger;
    }

    public async Task<ResearchResult> AnswerAsync(string question, WorkspaceContext workspaceContext)
    {
        var contextInfo = BuildContextInfo(workspaceContext);

        var systemPrompt = $"""
            You are a code research agent. Answer questions about the codebase using available tools.

            ## Your Environment
            {contextInfo}

            ## Your Capabilities
            You have access to these tools:
            - FileOps: Read files, list directories, search files
            - Git: Check status, view diffs, see commit history
            - CodeNav: Search code patterns, find definitions

            ## Instructions
            1. Use tools to gather information about the codebase
            2. Read relevant files to understand the code
            3. Provide accurate answers with file/line references
            4. If you can't find information, explain what you tried

            ## Response Format
            Provide a clear answer followed by references to specific files and line numbers.
            Include code snippets when relevant.
            End your response with <DONE> when finished.
            """;

        var userPrompt = $"Question: {question}";

        var chatHistory = new ChatHistory();
        chatHistory.AddSystemMessage(systemPrompt);
        chatHistory.AddUserMessage(userPrompt);

        var executionSettings = new OpenAIPromptExecutionSettings
        {
            MaxTokens = 16384,
            Temperature = 0.3,
            ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
        };

        // Execute research loop (similar to current agent but focused on research)
        const int maxIterations = 10;
        var iteration = 0;
        var finalAnswer = string.Empty;

        while (iteration < maxIterations)
        {
            iteration++;
            _logger.LogInformation("Research iteration {Iteration}", iteration);

            var response = await _chatService.GetChatMessageContentAsync(
                chatHistory,
                executionSettings: executionSettings,
                kernel: _kernel);

            var content = response.Content ?? "";
            chatHistory.AddAssistantMessage(content);

            _logger.LogDebug("Research response: {Response}", content);

            if (content.Contains("<DONE>", StringComparison.Ordinal))
            {
                finalAnswer = content.Replace("<DONE>", "").Trim();
                break;
            }

            // If no tools called and not first iteration, we're done
            var toolCallsDetected = response.Metadata?.ContainsKey("ToolCalls") == true;
            if (!toolCallsDetected && iteration > 1)
            {
                finalAnswer = content;
                break;
            }
        }

        // Parse the answer to extract references
        var references = ExtractReferences(finalAnswer);

        return new ResearchResult
        {
            Answer = finalAnswer,
            References = references,
            Confidence = 0.85 // Could be improved with actual confidence scoring
        };
    }

    private string BuildContextInfo(WorkspaceContext workspaceContext)
    {
        var lines = new List<string>();
        lines.Add("Workspace Context:");

        foreach (var (repoName, repoInfo) in workspaceContext.Repositories)
        {
            lines.Add($"  Repository: {repoName}");
            lines.Add($"    - Total Files: {repoInfo.TotalFiles}");

            if (repoInfo.KeyFiles.Any())
            {
                lines.Add("    - Key Files: " + string.Join(", ", repoInfo.KeyFiles.Take(5)));
            }
        }

        return string.Join("\n", lines);
    }

    private List<CodeReference> ExtractReferences(string answer)
    {
        // Simple reference extraction - looks for file paths and line numbers
        // Format: "filename.cs:123" or "filename.cs lines 45-67"
        var references = new List<CodeReference>();

        // This is a basic implementation - could be enhanced with regex
        // For now, return empty list as references would be embedded in the answer

        return references;
    }
}
