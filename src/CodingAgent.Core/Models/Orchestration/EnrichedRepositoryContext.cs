namespace CodingAgent.Models.Orchestration;

/// <summary>
/// Enriched repository context with analyzed project information
/// Built by the ContextAgent step
/// </summary>
public class EnrichedRepositoryContext
{
    /// <summary>
    /// Repository name
    /// </summary>
    public string RepositoryName { get; set; } = string.Empty;

    /// <summary>
    /// Repository path
    /// </summary>
    public string RepositoryPath { get; set; } = string.Empty;

    /// <summary>
    /// Total file count
    /// </summary>
    public int TotalFiles { get; set; }

    /// <summary>
    /// Detected project type (C# / .NET, Python, JavaScript/TypeScript, etc.)
    /// </summary>
    public string ProjectType { get; set; } = string.Empty;

    /// <summary>
    /// Build system (dotnet, npm, maven, gradle, etc.)
    /// </summary>
    public string BuildSystem { get; set; } = string.Empty;

    /// <summary>
    /// Test project paths
    /// </summary>
    public List<string> TestProjects { get; set; } = new();

    /// <summary>
    /// Source directories
    /// </summary>
    public List<string> SourceDirectories { get; set; } = new();

    /// <summary>
    /// Important file paths (solution files, package.json, etc.)
    /// </summary>
    public Dictionary<string, string> ImportantPaths { get; set; } = new();

    /// <summary>
    /// Top file extensions with counts
    /// </summary>
    public Dictionary<string, int> TopExtensions { get; set; } = new();

    /// <summary>
    /// Pre-formatted context summary for planning agent
    /// </summary>
    public string PlanningContextSummary { get; set; } = string.Empty;

    /// <summary>
    /// Pre-formatted context summary for execution agent
    /// </summary>
    public string ExecutionContextSummary { get; set; } = string.Empty;

    /// <summary>
    /// Whether the repository has test projects
    /// </summary>
    public bool HasTestProjects => TestProjects.Any();
}
