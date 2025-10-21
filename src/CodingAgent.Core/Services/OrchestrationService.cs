using CodingAgent.Agents.Orchestration;
using CodingAgent.Models;
using CodingAgent.Models.Orchestration;
using Microsoft.Extensions.Logging;

namespace CodingAgent.Services;

public interface IOrchestrationService
{
    Task<OrchestrationResult> ProcessRequestAsync(string input, string sessionId);
    Task<OrchestrationState?> GetStateAsync(string sessionId);
}

public class OrchestrationService : IOrchestrationService
{
    private readonly IIntentClassifierAgent _intentClassifier;
    private readonly IResearchAgent _researchAgent;
    private readonly IPlanningAgent _planningAgent;
    private readonly IExecutionAgent _executionAgent;
    private readonly ISummaryAgent _summaryAgent;
    private readonly ISessionStore _sessionStore;
    private readonly IWorkspaceManager _workspaceManager;
    private readonly ILogger<OrchestrationService> _logger;
    private readonly Dictionary<string, OrchestrationState> _stateCache = new();

    public OrchestrationService(
        IIntentClassifierAgent intentClassifier,
        IResearchAgent researchAgent,
        IPlanningAgent planningAgent,
        IExecutionAgent executionAgent,
        ISummaryAgent summaryAgent,
        ISessionStore sessionStore,
        IWorkspaceManager workspaceManager,
        ILogger<OrchestrationService> logger)
    {
        _intentClassifier = intentClassifier;
        _researchAgent = researchAgent;
        _planningAgent = planningAgent;
        _executionAgent = executionAgent;
        _summaryAgent = summaryAgent;
        _sessionStore = sessionStore;
        _workspaceManager = workspaceManager;
        _logger = logger;
    }

    public async Task<OrchestrationResult> ProcessRequestAsync(string input, string sessionId)
    {
        var startTime = DateTime.UtcNow;

        // Initialize state
        var workspaceContext = await _workspaceManager.ScanWorkspaceAsync();

        var state = new OrchestrationState
        {
            OriginalInput = input,
            SessionId = sessionId,
            WorkspaceContext = workspaceContext,
            StartTime = startTime,
            Status = AgentState.Working
        };

        _stateCache[sessionId] = state;

        try
        {
            await _sessionStore.AddConversationMessageAsync("user", input);
            await _sessionStore.AddStatusUpdateAsync(AgentState.Working, "Classifying intent");

            // Step 1: Classify Intent
            _logger.LogInformation("Step 1: Classifying intent for input: {Input}", input);
            var intentStart = DateTime.UtcNow;

            var intentResult = await _intentClassifier.ClassifyAsync(input, state.WorkspaceContext!);
            state.Intent = intentResult.Intent;
            state.IntentConfidence = intentResult.Confidence;
            state.Metrics["IntentClassificationTime"] = (DateTime.UtcNow - intentStart).TotalSeconds;

            await _sessionStore.AddThinkingAsync($"Intent classified: {intentResult.Intent} (Confidence: {intentResult.Confidence:F2})");

            // Step 2: Route to appropriate agent based on intent
            switch (intentResult.Intent)
            {
                case IntentType.Greeting:
                case IntentType.Unclear:
                    return await HandleSimpleReplyAsync(state, intentResult);

                case IntentType.Question:
                    return await HandleQuestionAsync(state);

                case IntentType.Task:
                    return await HandleTaskAsync(state);

                default:
                    throw new InvalidOperationException($"Unknown intent type: {intentResult.Intent}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in orchestration");
            state.Status = AgentState.Error;
            state.FinalResponse = $"Error: {ex.Message}";

            await _sessionStore.AddConversationMessageAsync("assistant", state.FinalResponse);

            return new OrchestrationResult
            {
                Response = state.FinalResponse,
                Status = state.Status,
                Metrics = state.Metrics
            };
        }
    }

    private async Task<OrchestrationResult> HandleSimpleReplyAsync(
        OrchestrationState state,
        IntentResult intentResult)
    {
        _logger.LogInformation("Handling simple reply for {Intent}", intentResult.Intent);

        var response = intentResult.Intent == IntentType.Greeting
            ? "Hello! I'm your coding agent. I can help you understand code, implement features, and manage your repositories. What would you like me to do?"
            : "I'm not sure I understand. Could you please provide more details about what you'd like me to do?";

        state.FinalResponse = response;
        state.Status = AgentState.Complete;
        state.EndTime = DateTime.UtcNow;

        await _sessionStore.AddConversationMessageAsync("assistant", response);
        await _sessionStore.AddStatusUpdateAsync(AgentState.Complete, "Task completed");

        return new OrchestrationResult
        {
            Response = response,
            Status = state.Status,
            Metrics = state.Metrics
        };
    }

    private async Task<OrchestrationResult> HandleQuestionAsync(OrchestrationState state)
    {
        _logger.LogInformation("Handling question: {Question}", state.OriginalInput);

        await _sessionStore.AddStatusUpdateAsync(AgentState.Working, "Researching answer");

        // Step 2: Research
        var researchStart = DateTime.UtcNow;
        var researchResult = await _researchAgent.AnswerAsync(state.OriginalInput, state.WorkspaceContext!);
        state.ResearchResult = researchResult;
        state.Metrics["ResearchTime"] = (DateTime.UtcNow - researchStart).TotalSeconds;

        await _sessionStore.AddThinkingAsync("Research completed");

        // Step 3: Summarize
        await _sessionStore.AddStatusUpdateAsync(AgentState.Working, "Creating summary");

        var summaryStart = DateTime.UtcNow;
        var summaryResult = await _summaryAgent.SummarizeResearchAsync(state.OriginalInput, researchResult);
        state.SummaryResult = summaryResult;
        state.Metrics["SummaryTime"] = (DateTime.UtcNow - summaryStart).TotalSeconds;

        state.FinalResponse = FormatResearchSummary(summaryResult, researchResult);
        state.Status = AgentState.Complete;
        state.EndTime = DateTime.UtcNow;

        await _sessionStore.AddConversationMessageAsync("assistant", state.FinalResponse);
        await _sessionStore.AddStatusUpdateAsync(AgentState.Complete, "Research completed");

        return new OrchestrationResult
        {
            Response = state.FinalResponse,
            Status = state.Status,
            Metrics = state.Metrics,
            Summary = summaryResult
        };
    }

    private async Task<OrchestrationResult> HandleTaskAsync(OrchestrationState state)
    {
        _logger.LogInformation("Handling task: {Task}", state.OriginalInput);

        // Step 2: Planning
        await _sessionStore.AddStatusUpdateAsync(AgentState.Working, "Creating execution plan");

        var planningStart = DateTime.UtcNow;
        var plan = await _planningAgent.CreatePlanAsync(state.OriginalInput, state.WorkspaceContext!);
        state.Plan = plan;
        state.Metrics["PlanningTime"] = (DateTime.UtcNow - planningStart).TotalSeconds;

        await _sessionStore.AddThinkingAsync($"Plan created with {plan.Steps.Count} steps");

        // Step 3: Execution
        await _sessionStore.AddStatusUpdateAsync(AgentState.Working, "Executing plan");

        var executionStart = DateTime.UtcNow;
        var stepResults = await _executionAgent.ExecutePlanAsync(plan, state.WorkspaceContext!);
        state.StepResults = stepResults;
        state.Metrics["ExecutionTime"] = (DateTime.UtcNow - executionStart).TotalSeconds;

        await _sessionStore.AddThinkingAsync($"Executed {stepResults.Count} steps");

        // Step 4: Summarize
        await _sessionStore.AddStatusUpdateAsync(AgentState.Working, "Creating summary");

        var summaryStart = DateTime.UtcNow;
        var totalExecutionTime = DateTime.UtcNow - executionStart;
        var summaryResult = await _summaryAgent.SummarizeTaskAsync(plan, stepResults, totalExecutionTime);
        state.SummaryResult = summaryResult;
        state.Metrics["SummaryTime"] = (DateTime.UtcNow - summaryStart).TotalSeconds;

        state.FinalResponse = FormatTaskSummary(summaryResult);
        state.Status = AgentState.Complete;
        state.EndTime = DateTime.UtcNow;

        await _sessionStore.AddConversationMessageAsync("assistant", state.FinalResponse);
        await _sessionStore.AddStatusUpdateAsync(AgentState.Complete, "Task completed");

        return new OrchestrationResult
        {
            Response = state.FinalResponse,
            Status = state.Status,
            Metrics = state.Metrics,
            Summary = summaryResult,
            Plan = plan,
            StepResults = stepResults
        };
    }

    private string FormatResearchSummary(SummaryResult summary, ResearchResult research)
    {
        var lines = new List<string>();

        if (!string.IsNullOrEmpty(summary.Summary))
        {
            lines.Add($"## {summary.Summary}");
            lines.Add("");
        }

        if (summary.KeyFindings.Any())
        {
            lines.Add("**Key Findings:**");
            foreach (var finding in summary.KeyFindings)
            {
                lines.Add($"- {finding}");
            }
            lines.Add("");
        }

        // Include full answer
        lines.Add(research.Answer);

        if (summary.FilesReferenced.Any())
        {
            lines.Add("");
            lines.Add("**Files Referenced:**");
            foreach (var file in summary.FilesReferenced)
            {
                lines.Add($"- {file}");
            }
        }

        return string.Join("\n", lines);
    }

    private string FormatTaskSummary(SummaryResult summary)
    {
        var lines = new List<string>();

        if (!string.IsNullOrEmpty(summary.Summary))
        {
            lines.Add($"## {summary.Summary}");
            lines.Add("");
        }

        if (summary.Accomplishments.Any())
        {
            lines.Add("**Accomplishments:**");
            foreach (var accomplishment in summary.Accomplishments)
            {
                lines.Add($"- {accomplishment}");
            }
            lines.Add("");
        }

        if (summary.FilesChanged != null)
        {
            var hasChanges = summary.FilesChanged.Created.Any() ||
                           summary.FilesChanged.Modified.Any() ||
                           summary.FilesChanged.Deleted.Any();

            if (hasChanges)
            {
                lines.Add("**Files Changed:**");
                foreach (var file in summary.FilesChanged.Created)
                    lines.Add($"- Created: {file}");
                foreach (var file in summary.FilesChanged.Modified)
                    lines.Add($"- Modified: {file}");
                foreach (var file in summary.FilesChanged.Deleted)
                    lines.Add($"- Deleted: {file}");
                lines.Add("");
            }
        }

        if (summary.Metrics != null)
        {
            lines.Add("**Metrics:**");
            lines.Add($"- Execution Time: {summary.Metrics.ExecutionTime}");
            lines.Add($"- Steps: {summary.Metrics.StepsCompleted}/{summary.Metrics.StepsTotal}");
            lines.Add($"- Success Rate: {summary.Metrics.SuccessRate}");
            lines.Add("");
        }

        if (summary.NextSteps.Any())
        {
            lines.Add("**Next Steps:**");
            foreach (var step in summary.NextSteps)
            {
                lines.Add($"- {step}");
            }
        }

        return string.Join("\n", lines);
    }

    public async Task<OrchestrationState?> GetStateAsync(string sessionId)
    {
        await Task.CompletedTask;
        return _stateCache.GetValueOrDefault(sessionId);
    }
}

public class OrchestrationResult
{
    public string Response { get; set; } = string.Empty;
    public AgentState Status { get; set; }
    public Dictionary<string, object> Metrics { get; set; } = new();
    public SummaryResult? Summary { get; set; }
    public ExecutionPlan? Plan { get; set; }
    public List<StepResult>? StepResults { get; set; }
}
