using System.Text.Json;
using System.Text.Json.Serialization;
using Azure;
using Azure.AI.OpenAI;
using CodingAgent.Core.Configuration;
using CodingAgent.Core.Models.Orchestration;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace CodingAgent.Core.Workflow.Executors;

/// <summary>
/// Executor that classifies user intent to determine workflow routing.
/// No tools needed - pure LLM classification.
/// </summary>
public sealed class IntentClassifierExecutor : Executor<ContextDiscoveryResult, IntentClassificationResult>
{
    private readonly AIAgent _agent;
    private readonly ModelSettings _modelSettings;
    private readonly ILogger<IntentClassifierExecutor> _logger;

    public IntentClassifierExecutor(
        ModelSettings modelSettings,
        ILogger<IntentClassifierExecutor> logger,
        ExecutorOptions? options = null,
        bool declareCrossRunShareable = false)
        : base(nameof(IntentClassifierExecutor), options, declareCrossRunShareable)
    {
        _modelSettings = modelSettings;
        _logger = logger;
        _agent = CreateAgent();
    }

    public override async ValueTask<IntentClassificationResult> HandleAsync(
        ContextDiscoveryResult message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var prompt = $$"""
            Classify this user request into one of these categories:
            - QUESTION: Information seeking about existing code
            - TASK: Request for code changes/implementation
            - GREETING: Casual conversation, status check
            - UNCLEAR: Ambiguous, needs clarification

            Respond ONLY with valid JSON:
            {"intent": "QUESTION" | "TASK" | "GREETING" | "UNCLEAR", "confidence": 0.0-1.0, "reasoning": "brief explanation"}

            User Request: {{message.OriginalInstruction}}
            """;

        var response = await _agent.RunAsync(prompt, cancellationToken: cancellationToken);
        var content = StripMarkdownFences(response.Text ?? "");

        try
        {
            var intentResult = JsonSerializer.Deserialize<IntentResult>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter() }
            }) ?? new IntentResult { Intent = IntentType.Unclear, Confidence = 0.5 };

            _logger.LogInformation("Intent classified: {Intent} (Confidence: {Confidence})",
                intentResult.Intent, intentResult.Confidence);

            return new IntentClassificationResult
            {
                Intent = intentResult.Intent,
                Confidence = intentResult.Confidence,
                Reasoning = intentResult.Reasoning,
                OriginalInstruction = message.OriginalInstruction,
                WorkspaceContext = message.WorkspaceContext
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse intent classification response");
            return new IntentClassificationResult
            {
                Intent = IntentType.Unclear,
                Confidence = 0.5,
                Reasoning = "Parse error",
                OriginalInstruction = message.OriginalInstruction,
                WorkspaceContext = message.WorkspaceContext
            };
        }
    }

    private AIAgent CreateAgent()
    {
        var client = new AzureOpenAIClient(
            new Uri(_modelSettings.Endpoint),
            new AzureKeyCredential(_modelSettings.ApiKey));

        var chatClient = client.GetChatClient(_modelSettings.IntentClassifier.Model).AsIChatClient();

        // Intent classifier needs no tools - pure LLM classification
        return chatClient.CreateAIAgent(
            instructions: "You are an intent classification agent. Classify user requests accurately.");
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
