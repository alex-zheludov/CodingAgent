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
    private Kernel? _kernel;
    private IChatCompletionService? _chatService;

    public SummaryAgentStep(
        IKernelFactory kernelFactory,
        ILogger<SummaryAgentStep> logger)
    {
        _kernelFactory = kernelFactory;
        _logger = logger;
    }

    [KernelFunction(Functions.SummarizeTask)]
    public async Task<SummaryResult> SummarizeTaskAsync(
        KernelProcessStepContext context,
        ExecutionPlan plan,
        List<StepResult> stepResults)
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

        var stepsSummary = string.Join("\n", stepResults.Select(s =>
            $"Step {s.StepId}: {s.Outcome} ({s.Status})"));

        var userPrompt = $"""
            Task: {plan.Task}
            Steps: {stepResults.Count(s => s.Status == StepStatus.Completed)}/{plan.Steps.Count}

            Results:
            {stepsSummary}

            Create JSON summary.
            """;

        var chatHistory = new ChatHistory();
        chatHistory.AddSystemMessage(systemPrompt);
        chatHistory.AddUserMessage(userPrompt);

        var response = await _chatService.GetChatMessageContentAsync(chatHistory, kernel: _kernel);
        var content = response.Content ?? "";

        try
        {
            var summary = JsonSerializer.Deserialize<SummaryResult>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? CreateFallbackTaskSummary(plan, stepResults);

            await context.EmitEventAsync(new KernelProcessEvent { Id = OutputEvents.SummaryReady, Data = summary });

            return summary;
        }
        catch (JsonException)
        {
            var fallback = CreateFallbackTaskSummary(plan, stepResults);
            await context.EmitEventAsync(new KernelProcessEvent { Id = OutputEvents.SummaryReady, Data = fallback });
            return fallback;
        }
    }

    [KernelFunction(Functions.SummarizeResearch)]
    public async Task<SummaryResult> SummarizeResearchAsync(
        KernelProcessStepContext context,
        string question,
        ResearchResult researchResult)
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
        chatHistory.AddUserMessage($"Question: {question}\nAnswer: {researchResult.Answer}");

        var response = await _chatService.GetChatMessageContentAsync(chatHistory, kernel: _kernel);
        var content = response.Content ?? "";

        try
        {
            var summary = JsonSerializer.Deserialize<SummaryResult>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? CreateFallbackResearchSummary(question, researchResult);

            await context.EmitEventAsync(new KernelProcessEvent { Id = OutputEvents.SummaryReady, Data = summary });

            return summary;
        }
        catch (JsonException)
        {
            var fallback = CreateFallbackResearchSummary(question, researchResult);
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
