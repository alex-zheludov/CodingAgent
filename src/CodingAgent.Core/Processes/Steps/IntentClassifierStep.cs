#pragma warning disable SKEXP0080 // SK Process Framework is experimental

using CodingAgent.Models.Orchestration;
using CodingAgent.Services;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Process;
using System.Text.Json;

namespace CodingAgent.Processes.Steps;

public class IntentClassifierStep : KernelProcessStep
{
    public static class Functions
    {
        public const string ClassifyIntent = nameof(ClassifyIntent);
    }

    public static class OutputEvents
    {
        public const string QuestionDetected = nameof(QuestionDetected);
        public const string TaskDetected = nameof(TaskDetected);
        public const string GreetingDetected = nameof(GreetingDetected);
        public const string UnclearDetected = nameof(UnclearDetected);
    }

    private readonly IKernelFactory _kernelFactory;
    private readonly ILogger<IntentClassifierStep> _logger;
    private Kernel? _kernel;
    private IChatCompletionService? _chatService;

    public IntentClassifierStep(
        IKernelFactory kernelFactory,
        ILogger<IntentClassifierStep> logger)
    {
        _kernelFactory = kernelFactory;
        _logger = logger;
    }

    [KernelFunction(Functions.ClassifyIntent)]
    public async Task<IntentResult> ClassifyIntentAsync(KernelProcessStepContext context, ProcessInput processInput)
    {
        // Lazy initialize kernel
        _kernel ??= _kernelFactory.CreateKernel(AgentCapability.IntentClassification);
        _chatService ??= _kernel.GetRequiredService<IChatCompletionService>();

        var systemPrompt = """
            You are an intent classification agent. Classify user requests into one of these categories:

            **QUESTION** - Information seeking about existing code
            **TASK** - Request for code changes/implementation
            **GREETING** - Casual conversation, status check
            **UNCLEAR** - Ambiguous, needs clarification

            Respond ONLY with valid JSON:
            {
              "intent": "QUESTION" | "TASK" | "GREETING" | "UNCLEAR",
              "confidence": 0.0-1.0,
              "reasoning": "brief explanation"
            }
            """;

        var userPrompt = $"User Request: {processInput.Input}\n\nClassify this request.";

        var chatHistory = new ChatHistory();
        chatHistory.AddSystemMessage(systemPrompt);
        chatHistory.AddUserMessage(userPrompt);

        var response = await _chatService.GetChatMessageContentAsync(chatHistory, kernel: _kernel);
        var content = response.Content ?? throw new InvalidOperationException("No response");

        try
        {
            var result = JsonSerializer.Deserialize<IntentResult>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(allowIntegerValues: false) }
            }) ?? throw new InvalidOperationException("Failed to deserialize");

            _logger.LogInformation("Intent: {Intent} (Confidence: {Confidence})", result.Intent, result.Confidence);

            // Emit the appropriate event based on intent with the correct data type
            switch (result.Intent)
            {
                case IntentType.Question:
                    var researchInput = new ResearchInput
                    {
                        Question = processInput.Input,
                        WorkspaceContext = processInput.WorkspaceContext,
                        Intent = result
                    };
                    await context.EmitEventAsync(new KernelProcessEvent { Id = OutputEvents.QuestionDetected, Data = researchInput });
                    break;

                case IntentType.Task:
                    var planningInput = new PlanningInput
                    {
                        Task = processInput.Input,
                        WorkspaceContext = processInput.WorkspaceContext,
                        Intent = result
                    };
                    await context.EmitEventAsync(new KernelProcessEvent { Id = OutputEvents.TaskDetected, Data = planningInput });
                    break;

                case IntentType.Greeting:
                case IntentType.Unclear:
                default:
                    // For greeting/unclear, we don't have specialized handlers yet, so just emit the result
                    var eventName = result.Intent == IntentType.Greeting ? OutputEvents.GreetingDetected : OutputEvents.UnclearDetected;
                    await context.EmitEventAsync(new KernelProcessEvent { Id = eventName, Data = result });
                    break;
            }

            return result;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse intent");
            var fallback = new IntentResult
            {
                Intent = IntentType.Unclear,
                Confidence = 0.5,
                Reasoning = "Parse error"
            };

            await context.EmitEventAsync(new KernelProcessEvent { Id = OutputEvents.UnclearDetected, Data = fallback });
            return fallback;
        }
    }
}
