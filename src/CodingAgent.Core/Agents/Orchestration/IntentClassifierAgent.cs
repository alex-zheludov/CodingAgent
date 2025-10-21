using CodingAgent.Models.Orchestration;
using CodingAgent.Services;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Text.Json;

namespace CodingAgent.Agents.Orchestration;

public interface IIntentClassifierAgent
{
    Task<IntentResult> ClassifyAsync(string input, WorkspaceContext workspaceContext);
}

public class IntentClassifierAgent : IIntentClassifierAgent
{
    private readonly Kernel _kernel;
    private readonly IChatCompletionService _chatService;
    private readonly ILogger<IntentClassifierAgent> _logger;

    public IntentClassifierAgent(
        IKernelFactory kernelFactory,
        ILogger<IntentClassifierAgent> logger)
    {
        _kernel = kernelFactory.CreateKernel(AgentCapability.IntentClassification);
        _chatService = _kernel.GetRequiredService<IChatCompletionService>();
        _logger = logger;
    }

    public async Task<IntentResult> ClassifyAsync(string input, WorkspaceContext workspaceContext)
    {
        var systemPrompt = """
            You are an intent classification agent. Classify user requests into one of these categories:

            **QUESTION** - Information seeking about existing code
            - Examples: "How does X work?", "Where is Y defined?", "What does Z do?"
            - User wants to understand existing code

            **TASK** - Request for code changes/implementation
            - Examples: "Add feature X", "Fix bug Y", "Refactor Z", "Create tests for..."
            - User wants you to modify, create, or change code

            **GREETING** - Casual conversation, status check, capability questions
            - Examples: "Hello", "What's your status?", "Are you ready?", "What can you do?"
            - User is greeting or asking about your capabilities

            **UNCLEAR** - Ambiguous, needs clarification
            - Examples: "Do something with the config", "Fix it"
            - Not enough context to determine intent

            Respond ONLY with valid JSON in this exact format:
            {
              "intent": "QUESTION" | "TASK" | "GREETING" | "UNCLEAR",
              "confidence": 0.0-1.0,
              "reasoning": "brief explanation",
              "suggestedAgent": "ResearchAgent" | "PlanningAgent" | "SimpleReply" | "Clarification"
            }

            Do not include any text before or after the JSON.
            """;

        var userPrompt = $"""
            User Request: {input}

            Workspace Context: {workspaceContext.Repositories.Count} repositories

            Classify this request and respond with JSON only.
            """;

        var chatHistory = new ChatHistory();
        chatHistory.AddSystemMessage(systemPrompt);
        chatHistory.AddUserMessage(userPrompt);

        var response = await _chatService.GetChatMessageContentAsync(chatHistory, kernel: _kernel);
        var content = response.Content ?? throw new InvalidOperationException("No response from intent classifier");

        _logger.LogInformation("Intent Classification Response: {Response}", content);

        // Parse JSON response
        try
        {
            var result = JsonSerializer.Deserialize<IntentResult>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result == null)
                throw new InvalidOperationException("Failed to deserialize intent result");

            _logger.LogInformation(
                "Classified intent: {Intent} (Confidence: {Confidence})",
                result.Intent,
                result.Confidence);

            return result;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse JSON response, falling back to UNCLEAR intent");
            return new IntentResult
            {
                Intent = IntentType.Unclear,
                Confidence = 0.5,
                Reasoning = "Failed to parse classification response",
                SuggestedAgent = "Clarification"
            };
        }
    }
}
