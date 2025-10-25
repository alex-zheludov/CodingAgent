using CodingAgent.Models.Orchestration;

namespace CodingAgent.Services;

/// <summary>
/// Service that builds enriched repository context from workspace context
/// Consolidates all project type detection, test project discovery, and context formatting
/// </summary>
public class RepositoryContextBuilder
{
    public EnrichedRepositoryContext BuildEnrichedContext(string repoName, RepositoryInfo repoInfo)
    {
        var context = new EnrichedRepositoryContext
        {
            RepositoryName = repoName,
            RepositoryPath = repoInfo.Path,
            TotalFiles = repoInfo.TotalFiles
        };

        // Detect project type
        context.ProjectType = DetectProjectType(repoInfo);

        // Detect build system
        context.BuildSystem = DetectBuildSystem(repoInfo);

        // Find test projects
        context.TestProjects = FindTestProjects(repoInfo);

        // Find source directories
        context.SourceDirectories = FindSourceDirectories(repoInfo);

        // Extract important paths
        context.ImportantPaths = ExtractImportantPaths(repoInfo);

        // Get top extensions
        context.TopExtensions = repoInfo.FilesByExtension
            .OrderByDescending(x => x.Value)
            .Take(5)
            .ToDictionary(x => x.Key, x => x.Value);

        // Build pre-formatted summaries
        context.PlanningContextSummary = BuildPlanningContextSummary(context, repoInfo);
        context.ExecutionContextSummary = BuildExecutionContextSummary(context);

        return context;
    }

    private string DetectProjectType(RepositoryInfo repoInfo)
    {
        if (repoInfo.FilesByExtension.ContainsKey(".cs") || repoInfo.FilesByExtension.ContainsKey(".csproj"))
            return "C# / .NET";

        if (repoInfo.FilesByExtension.ContainsKey(".py"))
            return "Python";

        if (repoInfo.FilesByExtension.ContainsKey(".js") || repoInfo.FilesByExtension.ContainsKey(".ts"))
            return "JavaScript/TypeScript";

        if (repoInfo.FilesByExtension.ContainsKey(".java"))
            return "Java";

        if (repoInfo.FilesByExtension.ContainsKey(".go"))
            return "Go";

        return string.Empty;
    }

    private string DetectBuildSystem(RepositoryInfo repoInfo)
    {
        if (repoInfo.KeyFiles.Any(f => f.EndsWith(".sln") || f.EndsWith(".csproj")))
            return "dotnet";

        if (repoInfo.KeyFiles.Any(f => f.EndsWith("package.json")))
            return "npm";

        if (repoInfo.KeyFiles.Any(f => f.EndsWith("pom.xml")))
            return "maven";

        if (repoInfo.KeyFiles.Any(f => f.EndsWith("build.gradle") || f.EndsWith("build.gradle.kts")))
            return "gradle";

        if (repoInfo.KeyFiles.Any(f => f.EndsWith("Makefile")))
            return "make";

        if (repoInfo.KeyFiles.Any(f => f.EndsWith("requirements.txt") || f.EndsWith("pyproject.toml")))
            return "pip";

        return string.Empty;
    }

    private List<string> FindTestProjects(RepositoryInfo repoInfo)
    {
        return repoInfo.KeyFiles
            .Where(f =>
                f.Contains("test", StringComparison.OrdinalIgnoreCase) ||
                f.Contains(".test.", StringComparison.OrdinalIgnoreCase) ||
                f.Contains("tests", StringComparison.OrdinalIgnoreCase) ||
                f.Contains("__tests__", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith("Test.csproj", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith("Tests.csproj", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private List<string> FindSourceDirectories(RepositoryInfo repoInfo)
    {
        var sourceIndicators = new[] { "src", "source", "lib", "app" };

        var directories = new HashSet<string>();

        foreach (var file in repoInfo.KeyFiles)
        {
            var parts = file.Split('/', '\\');
            foreach (var part in parts)
            {
                if (sourceIndicators.Contains(part, StringComparer.OrdinalIgnoreCase))
                {
                    directories.Add(part);
                }
            }
        }

        return directories.ToList();
    }

    private Dictionary<string, string> ExtractImportantPaths(RepositoryInfo repoInfo)
    {
        var paths = new Dictionary<string, string>();

        foreach (var file in repoInfo.KeyFiles)
        {
            if (file.EndsWith(".sln"))
                paths["solution"] = file;
            else if (file.EndsWith("package.json"))
                paths["packageJson"] = file;
            else if (file.EndsWith("pom.xml"))
                paths["pomXml"] = file;
            else if (file == "README.md")
                paths["readme"] = file;
            else if (file.Contains("appsettings.json"))
                paths["appSettings"] = file;
            else if (file.Contains("Program.cs") && !paths.ContainsKey("entryPoint"))
                paths["entryPoint"] = file;
            else if (file.Contains("main.py") && !paths.ContainsKey("entryPoint"))
                paths["entryPoint"] = file;
            else if (file.Contains("index.js") && !paths.ContainsKey("entryPoint"))
                paths["entryPoint"] = file;
        }

        return paths;
    }

    private string BuildPlanningContextSummary(EnrichedRepositoryContext context, RepositoryInfo repoInfo)
    {
        var lines = new List<string> { "Workspace:" };
        lines.Add($"  {context.RepositoryName}: {context.TotalFiles} files");

        // Add file type information
        if (context.TopExtensions.Any())
        {
            var topExtensions = context.TopExtensions
                .Select(x => $"{x.Key} ({x.Value})");
            lines.Add($"    File types: {string.Join(", ", topExtensions)}");
        }

        // Add key files information
        if (repoInfo.KeyFiles.Any())
        {
            lines.Add($"    Key files: {string.Join(", ", repoInfo.KeyFiles.Take(10))}");
        }

        if (!string.IsNullOrEmpty(context.ProjectType))
        {
            lines.Add($"    Project Type: {context.ProjectType}");
        }

        if (!string.IsNullOrEmpty(context.BuildSystem))
        {
            lines.Add($"    Build System: {context.BuildSystem}");
        }

        if (!context.HasTestProjects)
        {
            lines.Add($"    ⚠️ No test projects detected - DO NOT suggest running tests");
        }
        else
        {
            lines.Add($"    Test Projects: {context.TestProjects.Count} found");
        }

        return string.Join("\n", lines);
    }

    private string BuildExecutionContextSummary(EnrichedRepositoryContext context)
    {
        var lines = new List<string> { "=== WORKSPACE CONTEXT ===" };
        lines.Add($"Repository: {context.RepositoryName}");

        if (!string.IsNullOrEmpty(context.ProjectType))
        {
            lines.Add($"  Project Type: {context.ProjectType}");
        }

        if (!string.IsNullOrEmpty(context.BuildSystem))
        {
            lines.Add($"  Build System: {context.BuildSystem}");
        }

        // Add file types
        if (context.TopExtensions.Any())
        {
            var topExtensions = context.TopExtensions
                .Take(3)
                .Select(x => x.Key);
            lines.Add($"  Main file types: {string.Join(", ", topExtensions)}");
        }

        if (!context.HasTestProjects)
        {
            lines.Add($"  ⚠️ NO TEST PROJECTS - Do not attempt to run tests (dotnet test, npm test, etc.)");
        }
        else
        {
            lines.Add($"  Test Projects Available: {string.Join(", ", context.TestProjects.Take(3))}");
        }

        lines.Add("========================");
        return string.Join("\n", lines);
    }
}
