using CodingAgent.Models.Orchestration;
using CodingAgent.Services;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Text.Json;

namespace CodingAgent.Agents.Orchestration;

public interface ISummaryAgent
{
    Task<SummaryResult> SummarizeTaskAsync(ExecutionPlan plan, List<StepResult> stepResults, TimeSpan totalTime);
    Task<SummaryResult> SummarizeResearchAsync(string question, ResearchResult researchResult);
}

public class SummaryAgent : ISummaryAgent
{
    private readonly Kernel _kernel;
    private readonly IChatCompletionService _chatService;
    private readonly ILogger<SummaryAgent> _logger;

    public SummaryAgent(
        IKernelFactory kernelFactory,
        ILogger<SummaryAgent> logger)
    {
        _kernel = kernelFactory.CreateKernel(AgentCapability.Summary);
        _chatService = _kernel.GetRequiredService<IChatCompletionService>();
        _logger = logger;
    }

    public async Task<SummaryResult> SummarizeTaskAsync(ExecutionPlan plan, List<StepResult> stepResults, TimeSpan totalTime)
    {
        var systemPrompt = """
            You are a summary agent. Create concise, user-friendly summaries of completed tasks.

            Provide a summary in JSON format with:
            - summary: One-sentence overview
            - accomplishments: 3-5 bullet points of what was done
            - filesChanged: Lists of created/modified/deleted files
            - metrics: execution stats
            - nextSteps: 2-3 suggested follow-up actions

            Be concise and focus on outcomes, not process details.
            """;

        var stepsSummary = string.Join("\n", stepResults.Select(s =>
            $"Step {s.StepId}: {s.Outcome} (Status: {s.Status}, Files: {string.Join(", ", s.FilesModified)})"));

        var allModifiedFiles = stepResults
            .SelectMany(s => s.FilesModified)
            .Distinct()
            .ToList();

        var userPrompt = $"""
            Task: {plan.Task}
            Total Steps: {plan.Steps.Count}
            Completed Steps: {stepResults.Count(s => s.Status == StepStatus.Completed)}
            Execution Time: {totalTime.TotalSeconds:F1}s

            Step Results:
            {stepsSummary}

            Files Modified: {string.Join(", ", allModifiedFiles)}

            Create a summary in JSON format.
            """;

        var chatHistory = new ChatHistory();
        chatHistory.AddSystemMessage(systemPrompt);
        chatHistory.AddUserMessage(userPrompt);

        var response = await _chatService.GetChatMessageContentAsync(chatHistory, kernel: _kernel);
        var content = response.Content ?? "";

        _logger.LogInformation("Summary generated: {Summary}", content);

        try
        {
            var summary = JsonSerializer.Deserialize<SummaryResult>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return summary ?? CreateFallbackTaskSummary(plan, stepResults, totalTime, allModifiedFiles);
        }
        catch (JsonException)
        {
            return CreateFallbackTaskSummary(plan, stepResults, totalTime, allModifiedFiles);
        }
    }

    public async Task<SummaryResult> SummarizeResearchAsync(string question, ResearchResult researchResult)
    {
        var systemPrompt = """
            You are a summary agent. Create concise summaries of research findings.

            Provide a summary in JSON format with:
            - summary: One-sentence overview
            - keyFindings: 3-5 main points discovered
            - filesReferenced: List of files examined

            Be clear and highlight the most important information.
            """;

        var userPrompt = $"""
            Question: {question}
            Answer: {researchResult.Answer}
            References: {researchResult.References.Count} files

            Create a summary in JSON format.
            """;

        var chatHistory = new ChatHistory();
        chatHistory.AddSystemMessage(systemPrompt);
        chatHistory.AddUserMessage(userPrompt);

        var response = await _chatService.GetChatMessageContentAsync(chatHistory, kernel: _kernel);
        var content = response.Content ?? "";

        _logger.LogInformation("Research summary generated: {Summary}", content);

        try
        {
            var summary = JsonSerializer.Deserialize<SummaryResult>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return summary ?? CreateFallbackResearchSummary(question, researchResult);
        }
        catch (JsonException)
        {
            return CreateFallbackResearchSummary(question, researchResult);
        }
    }

    private SummaryResult CreateFallbackTaskSummary(
        ExecutionPlan plan,
        List<StepResult> stepResults,
        TimeSpan totalTime,
        List<string> allModifiedFiles)
    {
        var completedSteps = stepResults.Count(s => s.Status == StepStatus.Completed);
        var successRate = stepResults.Count > 0
            ? $"{(completedSteps * 100 / stepResults.Count)}%"
            : "N/A";

        return new SummaryResult
        {
            Summary = $"Completed {completedSteps}/{plan.Steps.Count} steps for: {plan.Task}",
            Accomplishments = stepResults
                .Where(s => s.Status == StepStatus.Completed)
                .Select(s => s.Outcome)
                .Take(5)
                .ToList(),
            FilesChanged = new FileChanges
            {
                Created = new List<string>(),
                Modified = allModifiedFiles,
                Deleted = new List<string>()
            },
            Metrics = new SummaryMetrics
            {
                ExecutionTime = $"{totalTime.TotalSeconds:F1}s",
                StepsCompleted = completedSteps,
                StepsTotal = plan.Steps.Count,
                SuccessRate = successRate
            },
            NextSteps = new List<string> { "Review changes", "Run tests" }
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
