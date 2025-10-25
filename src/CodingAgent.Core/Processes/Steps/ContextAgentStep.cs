#pragma warning disable SKEXP0080 // SK Process Framework is experimental

using CodingAgent.Models.Orchestration;
using CodingAgent.Services;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Process;
using System.Text.Json;

namespace CodingAgent.Processes.Steps;

/// <summary>
/// LLM-powered context agent that explores the codebase to build enriched context
/// Uses FileOps and CodeNav plugins to intelligently analyze the repository
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

    private readonly IKernelFactory _kernelFactory;
    private readonly ILogger<ContextAgentStep> _logger;
    private Kernel? _kernel;
    private IChatCompletionService? _chatService;

    public ContextAgentStep(
        IKernelFactory kernelFactory,
        ILogger<ContextAgentStep> _logger)
    {
        _kernelFactory = kernelFactory;
        this._logger = _logger;
    }

    [KernelFunction(Functions.BuildContext)]
    public async Task<string> BuildContextAsync(
        KernelProcessStepContext context,
        PlanningInput input)
    {
        _kernel ??= _kernelFactory.CreateKernel(AgentCapability.Research);
        _chatService ??= _kernel.GetRequiredService<IChatCompletionService>();

        _logger.LogInformation("Context agent exploring {RepoCount} repositories",
            input.WorkspaceContext.Repositories.Count);

        var systemPrompt = $$"""
            You are a context analysis agent. Your job is to explore the codebase and build a comprehensive context summary
            that will help other agents (planning and execution agents) understand the project structure.

            ## Available Repositories
            {{BuildRepositoryList(input.WorkspaceContext)}}

            ## Your Task
            Use the available tools to explore the codebase and gather:
            1. **Project Type** - What language/framework (C#/.NET, Python, Node.js, etc.)
            2. **Build System** - How to build (dotnet, npm, maven, etc.)
            3. **Test Projects** - Where are tests located, what test frameworks are used
            4. **Source Structure** - Key directories (src/, lib/, app/, etc.)
            5. **Entry Points** - Main program files (Program.cs, main.py, index.js, etc.)
            6. **Important Files** - Configuration files, solution files, package manifests
            7. **Project Dependencies** - What major libraries/frameworks are used

            ## Response Format (JSON only)
            Return your analysis as JSON:
            {
              "repositories": [
                {
                  "name": "RepoName",
                  "projectType": "C# / .NET",
                  "buildSystem": "dotnet",
                  "testProjects": ["path/to/Test.csproj"],
                  "sourceDirectories": ["src", "lib"],
                  "entryPoints": ["src/Program.cs"],
                  "importantFiles": {
                    "solution": "path/to/Solution.sln",
                    "config": "appsettings.json"
                  },
                  "dependencies": ["Microsoft.AspNetCore", "Entity Framework"],
                  "planningContextSummary": "Formatted summary for planning agent...",
                  "executionContextSummary": "Formatted summary for execution agent..."
                }
              ]
            }

            ## Instructions
            - Use FileOps.GetWorkspaceOverview to get initial overview
            - Use FileOps.SearchFiles or CodeNav.SearchCode to find specific files
            - Use FileOps.ReadFile to examine important files (solution files, manifests, etc.)
            - Build comprehensive context that will help downstream agents
            - End with <CONTEXT COMPLETE>
            """;

        var chatHistory = new ChatHistory();
        chatHistory.AddSystemMessage(systemPrompt);
        chatHistory.AddUserMessage("Explore the workspace and build comprehensive context for the planning and execution agents.");

        var executionSettings = new OpenAIPromptExecutionSettings
        {
            MaxTokens = 4096,
            Temperature = 0.2,
            ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
        };

        const int maxIterations = 15;
        var contextJson = string.Empty;

        for (int iteration = 0; iteration < maxIterations; iteration++)
        {
            var response = await _chatService.GetChatMessageContentAsync(
                chatHistory,
                executionSettings: executionSettings,
                kernel: _kernel);

            var content = response.Content ?? "";
            chatHistory.AddAssistantMessage(content);

            if (content.Contains("<CONTEXT COMPLETE>", StringComparison.Ordinal))
            {
                contextJson = content.Replace("<CONTEXT COMPLETE>", "").Trim();
                break;
            }

            var toolCallsDetected = response.Metadata?.ContainsKey("ToolCalls") == true;
            if (!toolCallsDetected && iteration > 0)
            {
                contextJson = content;
                break;
            }
        }

        _logger.LogInformation("Context agent completed exploration. Response length: {Length}", contextJson.Length);

        // Parse and emit enriched context
        var enrichedInput = ParseContextResponse(contextJson, input);

        await context.EmitEventAsync(new KernelProcessEvent
        {
            Id = OutputEvents.ContextReady,
            Data = enrichedInput
        });

        return contextJson;
    }

    private string BuildRepositoryList(WorkspaceContext workspaceContext)
    {
        var lines = new List<string>();
        foreach (var (repoName, repoInfo) in workspaceContext.Repositories)
        {
            lines.Add($"- **{repoName}**: {repoInfo.Path} ({repoInfo.TotalFiles} files)");
        }
        return string.Join("\n", lines);
    }

    private EnrichedPlanningInput ParseContextResponse(string contextJson, PlanningInput input)
    {
        try
        {
            // Strip markdown code fences if present
            var jsonContent = contextJson.Trim();
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

            var response = JsonSerializer.Deserialize<ContextAnalysisResponse>(jsonContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            var enrichedContexts = new Dictionary<string, EnrichedRepositoryContext>();

            if (response?.Repositories != null)
            {
                foreach (var repo in response.Repositories)
                {
                    enrichedContexts[repo.RepositoryName] = repo;
                }
            }

            return new EnrichedPlanningInput
            {
                Task = input.Task,
                WorkspaceContext = input.WorkspaceContext,
                EnrichedContexts = enrichedContexts,
                Intent = input.Intent
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse context JSON, using fallback");

            // Fallback: create basic context
            var fallbackContexts = new Dictionary<string, EnrichedRepositoryContext>();
            foreach (var (repoName, repoInfo) in input.WorkspaceContext.Repositories)
            {
                fallbackContexts[repoName] = new EnrichedRepositoryContext
                {
                    RepositoryName = repoName,
                    RepositoryPath = repoInfo.Path,
                    TotalFiles = repoInfo.TotalFiles,
                    PlanningContextSummary = $"Repository: {repoName} ({repoInfo.TotalFiles} files)",
                    ExecutionContextSummary = $"Repository: {repoName}"
                };
            }

            return new EnrichedPlanningInput
            {
                Task = input.Task,
                WorkspaceContext = input.WorkspaceContext,
                EnrichedContexts = fallbackContexts,
                Intent = input.Intent
            };
        }
    }
}

/// <summary>
/// Response from context analysis agent
/// </summary>
public class ContextAnalysisResponse
{
    public List<EnrichedRepositoryContext> Repositories { get; set; } = new();
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
