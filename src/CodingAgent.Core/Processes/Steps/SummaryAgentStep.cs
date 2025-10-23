#pragma warning disable SKEXP0080 // SK Process Framework is experimental

using CodingAgent.Models.Orchestration;
using CodingAgent.Services;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Process;
using System.Text.Json;

namespace CodingAgent.Processes.Steps;

public class SummaryAgentStep : KernelProcessStep
{
    public static class Functions
    {
        public const string SummarizeTask = nameof(SummarizeTask);
        public const string SummarizeResearch = nameof(SummarizeResearch);
    }

    public static class OutputEvents
    {
        public const string SummaryReady = nameof(SummaryReady);
    }

    private readonly IKernelFactory _kernelFactory;
    private readonly ILogger<SummaryAgentStep> _logger;
    private readonly Action<SummaryResult>? _captureSummary;
    private Kernel? _kernel;
    private IChatCompletionService? _chatService;

    public SummaryAgentStep(
        IKernelFactory kernelFactory,
        ILogger<SummaryAgentStep> logger,
        Action<SummaryResult>? captureSummary = null)
    {
        _kernelFactory = kernelFactory;
        _logger = logger;
        _captureSummary = captureSummary;
    }

    [KernelFunction(Functions.SummarizeTask)]
    public async Task<SummaryResult> SummarizeTaskAsync(
        KernelProcessStepContext context,
        SummaryTaskInput input)
    {
        _kernel ??= _kernelFactory.CreateKernel(AgentCapability.Summary);
        _chatService ??= _kernel.GetRequiredService<IChatCompletionService>();

        var systemPrompt = """
            Create a concise summary in JSON format:
            {
              "summary": "one sentence overview",
              "accomplishments": ["item1", "item2"],
              "filesChanged": { "created": [], "modified": [], "deleted": [] },
              "metrics": { "executionTime": "5s", "stepsCompleted": 3, "stepsTotal": 3, "successRate": "100%" },
              "nextSteps": ["suggestion1"]
            }
            """;

        var stepsSummary = string.Join("\n", input.Steps.Select(s =>
            $"Step {s.StepId}: {s.Outcome} ({s.Status})"));

        var userPrompt = $"""
            Task: {input.Plan.Task}
            Steps: {input.Steps.Count(s => s.Status == StepStatus.Completed)}/{input.Plan.Steps.Count}

            Results:
            {stepsSummary}

            Create JSON summary.
            """;

        var chatHistory = new ChatHistory();
        chatHistory.AddSystemMessage(systemPrompt);
        chatHistory.AddUserMessage(userPrompt);

        var response = await _chatService.GetChatMessageContentAsync(chatHistory, kernel: _kernel);
        var content = response.Content ?? "";

        // Strip markdown code fences if present
        var jsonContent = content.Trim();
        if (jsonContent.StartsWith("```"))
        {
            var firstLineEnd = jsonContent.IndexOf('\n');
            if (firstLineEnd > 0)
            {
                jsonContent = jsonContent.Substring(firstLineEnd + 1);
            }

            var lastFenceIndex = jsonContent.LastIndexOf("```");
            if (lastFenceIndex > 0)
            {
                jsonContent = jsonContent.Substring(0, lastFenceIndex);
            }

            jsonContent = jsonContent.Trim();
        }

        try
        {
            var summary = JsonSerializer.Deserialize<SummaryResult>(jsonContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? CreateFallbackTaskSummary(input.Plan, input.Steps);

            // Capture the summary for the orchestration service
            _captureSummary?.Invoke(summary);
            _logger.LogInformation("Task summary created: {Summary}", summary.Summary);

            await context.EmitEventAsync(new KernelProcessEvent { Id = OutputEvents.SummaryReady, Data = summary });

            return summary;
        }
        catch (JsonException)
        {
            var fallback = CreateFallbackTaskSummary(input.Plan, input.Steps);

            // Capture the fallback summary
            _captureSummary?.Invoke(fallback);
            _logger.LogInformation("Task summary created (fallback): {Summary}", fallback.Summary);

            await context.EmitEventAsync(new KernelProcessEvent { Id = OutputEvents.SummaryReady, Data = fallback });
            return fallback;
        }
    }

    [KernelFunction(Functions.SummarizeResearch)]
    public async Task<SummaryResult> SummarizeResearchAsync(
        KernelProcessStepContext context,
        SummaryResearchInput input)
    {
        _kernel ??= _kernelFactory.CreateKernel(AgentCapability.Summary);
        _chatService ??= _kernel.GetRequiredService<IChatCompletionService>();

        var systemPrompt = """
            Create a concise summary in JSON format:
            {
              "summary": "one sentence overview",
              "keyFindings": ["finding1", "finding2"],
              "filesReferenced": ["file1"]
            }
            """;

        var chatHistory = new ChatHistory();
        chatHistory.AddSystemMessage(systemPrompt);
        chatHistory.AddUserMessage($"Question: {input.OriginalQuestion}\nAnswer: {input.Research.Answer}");

        _logger.LogInformation("Summarizing research. Answer length: {Length}", input.Research.Answer.Length);

        var response = await _chatService.GetChatMessageContentAsync(chatHistory, kernel: _kernel);
        var content = response.Content ?? "";

        _logger.LogInformation("Summary response length: {Length}, Content preview: {Preview}",
            content.Length, content.Length > 100 ? content.Substring(0, 100) : content);

        // Strip markdown code fences if present (```json ... ``` or ``` ... ```)
        var jsonContent = content.Trim();
        if (jsonContent.StartsWith("```"))
        {
            // Remove opening fence (```json or ```)
            var firstLineEnd = jsonContent.IndexOf('\n');
            if (firstLineEnd > 0)
            {
                jsonContent = jsonContent.Substring(firstLineEnd + 1);
            }

            // Remove closing fence (```)
            var lastFenceIndex = jsonContent.LastIndexOf("```");
            if (lastFenceIndex > 0)
            {
                jsonContent = jsonContent.Substring(0, lastFenceIndex);
            }

            jsonContent = jsonContent.Trim();
            _logger.LogInformation("Stripped markdown fences. New length: {Length}", jsonContent.Length);
        }

        try
        {
            var summary = JsonSerializer.Deserialize<SummaryResult>(jsonContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? CreateFallbackResearchSummary(input.OriginalQuestion, input.Research);

            // Capture the summary for the orchestration service
            _captureSummary?.Invoke(summary);
            _logger.LogInformation("Research summary created: {Summary}", summary.Summary);

            await context.EmitEventAsync(new KernelProcessEvent { Id = OutputEvents.SummaryReady, Data = summary });

            return summary;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize summary JSON. Using fallback. Response was: {Response}",
                content.Length > 500 ? content.Substring(0, 500) : content);

            var fallback = CreateFallbackResearchSummary(input.OriginalQuestion, input.Research);

            // Capture the fallback summary
            _captureSummary?.Invoke(fallback);
            _logger.LogInformation("Research summary created (fallback): {Summary}", fallback.Summary);

            await context.EmitEventAsync(new KernelProcessEvent { Id = OutputEvents.SummaryReady, Data = fallback });
            return fallback;
        }
    }

    private SummaryResult CreateFallbackTaskSummary(ExecutionPlan plan, List<StepResult> stepResults)
    {
        var completed = stepResults.Count(s => s.Status == StepStatus.Completed);
        return new SummaryResult
        {
            Summary = $"Completed {completed}/{plan.Steps.Count} steps for: {plan.Task}",
            Accomplishments = stepResults.Where(s => s.Status == StepStatus.Completed)
                .Select(s => s.Outcome).Take(5).ToList(),
            Metrics = new SummaryMetrics
            {
                StepsCompleted = completed,
                StepsTotal = plan.Steps.Count,
                SuccessRate = $"{(completed * 100 / Math.Max(plan.Steps.Count, 1))}%"
            }
        };
    }

    private SummaryResult CreateFallbackResearchSummary(string question, ResearchResult researchResult)
    {
        return new SummaryResult
        {
            Summary = $"Answered: {question}",
            KeyFindings = new List<string> { researchResult.Answer },
            FilesReferenced = researchResult.References.Select(r => r.File).Distinct().ToList()
        };
    }
}
