using Azure;
using Azure.AI.OpenAI;
using CodingAgent.Core.Configuration;
using CodingAgent.Core.Models.Orchestration;
using CodingAgent.Core.Plugins;
using CodingAgent.Core.Services;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace CodingAgent.Core.Workflow.Executors;

/// <summary>
/// Executor that implements plan steps using all available tools.
/// </summary>
public sealed class ImplementationExecutor : Executor<PlanningResult, SummaryResult>
{
    private readonly AIAgent _agent;
    private readonly ModelSettings _modelSettings;
    private readonly AgentSettings _agentSettings;
    private readonly IFileOperationsPlugin _fileOperations;
    private readonly ICodeNavigationPlugin _codeNavigation;
    private readonly IGitPlugin _git;
    private readonly ICommandPlugin _command;
    private readonly ILogger<ImplementationExecutor> _logger;

    public ImplementationExecutor(
        ModelSettings modelSettings,
        AgentSettings agentSettings,
        ILogger<ImplementationExecutor> logger,
        IFileOperationsPlugin fileOperations,
        ICodeNavigationPlugin codeNavigation,
        IGitPlugin git,
        ICommandPlugin command,
        ExecutorOptions? options = null,
        bool declareCrossRunShareable = false)
        : base(nameof(ImplementationExecutor), options, declareCrossRunShareable)
    {
        _modelSettings = modelSettings;
        _agentSettings = agentSettings;
        _logger = logger;
        _fileOperations = fileOperations;
        _codeNavigation = codeNavigation;
        _git = git;
        _command = command;
        _agent = CreateAgent();
    }

    public override async ValueTask<SummaryResult> HandleAsync(
        PlanningResult message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var results = new List<StepResult>();

        foreach (var step in message.Plan.Steps.OrderBy(s => s.StepId))
        {
            var dependenciesMet = step.Dependencies.All(depId =>
                results.Any(r => r.StepId == depId && r.Status == StepStatus.Completed));

            if (!dependenciesMet)
            {
                results.Add(new StepResult { StepId = step.StepId, Status = StepStatus.Skipped, Outcome = "Dependencies not met" });
                continue;
            }

            _logger.LogInformation("Executing step {StepId}: {Action}", step.StepId, step.Action);

            var prompt = $"""
                Execute this step using available tools:
                Step: {step.Description}
                Expected outcome: {step.ExpectedOutcome}

                {message.WorkspaceContext?.DiscoveredContext}

                Execute: {step.Action}

                IMPORTANT: After using any tool, carefully review its output:
                - Check for error messages, exit codes, or failure indicators
                - If a command fails, READ THE ERROR MESSAGE carefully
                - If the error suggests a correction (wrong parameter, invalid value, typo), try again with the correction
                - You may retry a command with DIFFERENT parameters if the error message tells you what's wrong
                - DO NOT retry the exact same command that just failed
                - If you cannot fix the error after reviewing the message, explain what went wrong and end with <STEP FAILED>
                - If a file is missing or a prerequisite is not met that you cannot fix, end with <STEP FAILED>

                End with <STEP COMPLETE> when the step succeeds, or <STEP FAILED> if it cannot be completed.
                """;

            var thread = _agent.GetNewThread();
            string outcome = "";
            var startTime = DateTime.UtcNow;

            try
            {
                bool stepFailed = false;
                for (int i = 0; i < _agentSettings.MaxIterationsPerStep; i++)
                {
                    var userInput = i == 0 ? prompt : "Continue execution. End with <STEP COMPLETE> when done, or <STEP FAILED> if it fails.";
                    var response = await _agent.RunAsync(userInput, thread, cancellationToken: cancellationToken);
                    var content = response.Text ?? "";

                    if (content.Contains("<STEP FAILED>"))
                    {
                        outcome = content.Replace("<STEP FAILED>", "").Trim();
                        stepFailed = true;
                        break;
                    }

                    if (content.Contains("<STEP COMPLETE>"))
                    {
                        outcome = content.Replace("<STEP COMPLETE>", "").Trim();
                        break;
                    }

                    if (!string.IsNullOrEmpty(content) && i > 0)
                    {
                        outcome = content;
                        break;
                    }
                }

                results.Add(new StepResult
                {
                    StepId = step.StepId,
                    Status = stepFailed ? StepStatus.Failed : StepStatus.Completed,
                    Outcome = outcome.Length > 500 ? outcome[..500] : outcome,
                    ExecutionTime = DateTime.UtcNow - startTime
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing step {StepId}", step.StepId);
                results.Add(new StepResult
                {
                    StepId = step.StepId,
                    Status = StepStatus.Failed,
                    Outcome = $"Error: {ex.Message}",
                    ExecutionTime = DateTime.UtcNow - startTime
                });
            }
        }

        // Build summary
        var completed = results.Count(r => r.Status == StepStatus.Completed);
        var failed = results.Count(r => r.Status == StepStatus.Failed);
        var total = message.Plan.Steps.Count;

        var summary = new SummaryResult
        {
            Summary = $"Completed {completed}/{total} steps for: {message.OriginalTask}",
            Accomplishments = results
                .Where(r => r.Status == StepStatus.Completed)
                .Select(r => r.Outcome)
                .Take(5)
                .ToList(),
            Metrics = new SummaryMetrics
            {
                StepsCompleted = completed,
                StepsTotal = total,
                SuccessRate = total > 0 ? $"{completed * 100 / total}%" : "100%"
            },
            Plan = message.Plan
        };

        if (failed > 0)
        {
            summary.KeyFindings = results
                .Where(r => r.Status == StepStatus.Failed)
                .Select(r => $"Step {r.StepId} failed: {r.Outcome}")
                .Take(3)
                .ToList();
        }

        return summary;
    }

    private AIAgent CreateAgent()
    {
        var client = new AzureOpenAIClient(
            new Uri(_modelSettings.Endpoint),
            new AzureKeyCredential(_modelSettings.ApiKey));

        var chatClient = client.GetChatClient(_modelSettings.Execution.Model).AsIChatClient();

        // Execution agent gets all tools
        var tools = new List<AITool>
        {
            // File Operations - all
            AIFunctionFactory.Create(_fileOperations.ReadFileAsync),
            AIFunctionFactory.Create(_fileOperations.WriteFileAsync),
            AIFunctionFactory.Create(_fileOperations.ListDirectoryAsync),
            AIFunctionFactory.Create(_fileOperations.FindFilesAsync),

            // CodeNavivagtion
            AIFunctionFactory.Create(_codeNavigation.GetWorkspaceOverviewAsync),
            AIFunctionFactory.Create(_codeNavigation.GetDirectoryTreeAsync),
            AIFunctionFactory.Create(_codeNavigation.SearchCodeAsync),
            AIFunctionFactory.Create(_codeNavigation.FindDefinitionsAsync),

            // Git - all
            AIFunctionFactory.Create(_git.GetStatusAsync),
            AIFunctionFactory.Create(_git.GetDiffAsync),
            AIFunctionFactory.Create(_git.GetCommitHistoryAsync),
            AIFunctionFactory.Create(_git.StageFilesAsync),
            AIFunctionFactory.Create(_git.CommitAsync),
            AIFunctionFactory.Create(_git.CreateBranchAsync),
            AIFunctionFactory.Create(_git.PushAsync),

            // Command
            AIFunctionFactory.Create(_command.ExecuteCommandAsync),
            AIFunctionFactory.Create(_command.BuildDotnetAsync),
            AIFunctionFactory.Create(_command.TestDotnetAsync),
            AIFunctionFactory.Create(_command.NpmInstallAsync),
            AIFunctionFactory.Create(_command.NpmTestAsync),
        };

        return chatClient.CreateAIAgent(
            instructions: "You are an implementation agent that implements coding tasks step by step.",
            tools: tools);
    }
}
