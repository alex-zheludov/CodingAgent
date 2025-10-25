#pragma warning disable SKEXP0080 // SK Process Framework is experimental

using CodingAgent.Models.Orchestration;
using CodingAgent.Services;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Process;

namespace CodingAgent.Processes.Steps;

/// <summary>
/// Builds enriched repository context for planning and execution agents
/// Consolidates all project type detection, test discovery, and context formatting
/// </summary>
public class ContextAgentStep : KernelProcessStep
{
    public static class Functions
    {
        public const string BuildContext = nameof(BuildContext);
    }

    public static class OutputEvents
    {
        public const string ContextReady = nameof(ContextReady);
    }

    private readonly RepositoryContextBuilder _contextBuilder;
    private readonly ILogger<ContextAgentStep> _logger;

    public ContextAgentStep(
        RepositoryContextBuilder contextBuilder,
        ILogger<ContextAgentStep> logger)
    {
        _contextBuilder = contextBuilder;
        _logger = logger;
    }

    [KernelFunction(Functions.BuildContext)]
    public async Task<Dictionary<string, EnrichedRepositoryContext>> BuildContextAsync(
        KernelProcessStepContext context,
        PlanningInput input)
    {
        _logger.LogInformation("Building enriched context for {RepoCount} repositories",
            input.WorkspaceContext.Repositories.Count);

        var enrichedContexts = new Dictionary<string, EnrichedRepositoryContext>();

        foreach (var (repoName, repoInfo) in input.WorkspaceContext.Repositories)
        {
            var enrichedContext = _contextBuilder.BuildEnrichedContext(repoName, repoInfo);
            enrichedContexts[repoName] = enrichedContext;

            _logger.LogInformation("Built context for {RepoName}: {ProjectType}, Build: {BuildSystem}, Tests: {TestCount}",
                repoName,
                enrichedContext.ProjectType,
                enrichedContext.BuildSystem,
                enrichedContext.TestProjects.Count);
        }

        // Create enriched planning input
        var enrichedInput = new EnrichedPlanningInput
        {
            Task = input.Task,
            WorkspaceContext = input.WorkspaceContext,
            EnrichedContexts = enrichedContexts,
            Intent = input.Intent
        };

        await context.EmitEventAsync(new KernelProcessEvent
        {
            Id = OutputEvents.ContextReady,
            Data = enrichedInput
        });

        return enrichedContexts;
    }
}

/// <summary>
/// Planning input with enriched repository contexts
/// </summary>
public class EnrichedPlanningInput
{
    public string Task { get; set; } = string.Empty;
    public WorkspaceContext WorkspaceContext { get; set; } = null!;
    public Dictionary<string, EnrichedRepositoryContext> EnrichedContexts { get; set; } = new();
    public IntentResult? Intent { get; set; }
}
