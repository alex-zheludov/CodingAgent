using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using CodingAgent.Core.Services;

namespace CodingAgent.Core.Plugins;

public interface ICodeNavigationPlugin
{
    Task<string> GetWorkspaceOverviewAsync();

    Task<string> GetDirectoryTreeAsync(
        [Description("The repository name")] string repositoryName,
        [Description("Optional: subdirectory path to start from")] string? subdirectory = null,
        [Description("Optional: maximum depth (default 3)")] int maxDepth = 3);

    Task<string> SearchCodeAsync(
        [Description("The regex pattern or text to search for")] string searchPattern,
        [Description("Optional: file pattern to filter (e.g., *.cs)")] string filePattern = "*",
        [Description("Optional: specific repository name, or all if not specified")] string? repositoryName = null);

    Task<string> FindDefinitionsAsync(
        [Description("The name of the class, interface, or method to find")] string definitionName,
        [Description("Optional: specific repository name")] string? repositoryName = null);
}

public class CodeNavigationPlugin : ICodeNavigationPlugin
{
    private readonly IWorkspaceManager _workspaceManager;
    private readonly ISecurityService _securityService;
    private readonly ILogger<CodeNavigationPlugin> _logger;

    public CodeNavigationPlugin(
        IWorkspaceManager workspaceManager,
        ISecurityService securityService,
        ILogger<CodeNavigationPlugin> logger)
    {
        _workspaceManager = workspaceManager;
        _securityService = securityService;
        _logger = logger;
    }

    [Description("Get an overview of the workspace structure including all repositories and key files")]
    public async Task<string> GetWorkspaceOverviewAsync()
    {
        try
        {
            var context = await _workspaceManager.ScanWorkspaceAsync();
            var sb = new StringBuilder();

            sb.AppendLine($"Workspace scanned at: {context.ScanTime}");
            sb.AppendLine($"Total repositories: {context.Repositories.Count}");
            sb.AppendLine();

            foreach (var (repoName, repoInfo) in context.Repositories)
            {
                sb.AppendLine($"Repository: {repoName}");
                sb.AppendLine($"  Path: {repoInfo.Path}");
                sb.AppendLine($"  Total files: {repoInfo.TotalFiles}");

                if (repoInfo.FilesByExtension.Count > 0)
                {
                    sb.AppendLine("  File types:");
                    foreach (var (ext, count) in repoInfo.FilesByExtension.OrderByDescending(x => x.Value).Take(10))
                    {
                        sb.AppendLine($"    {ext}: {count} files");
                    }
                }

                if (repoInfo.KeyFiles.Count > 0)
                {
                    sb.AppendLine("  Key files:");
                    foreach (var file in repoInfo.KeyFiles.Take(20))
                    {
                        sb.AppendLine($"    {file}");
                    }
                }

                sb.AppendLine();
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting workspace overview");
            return $"Error: {ex.Message}";
        }
    }

    [Description("Get a directory tree structure for a repository")]
    public async Task<string> GetDirectoryTreeAsync(
        [Description("The repository name")] string repositoryName,
        [Description("Optional: subdirectory path to start from")] string? subdirectory = null,
        [Description("Optional: maximum depth (default 3)")] int maxDepth = 3)
    {
        try
        {
            var repoPath = _workspaceManager.GetRepositoryPath(repositoryName);
            var startPath = string.IsNullOrWhiteSpace(subdirectory)
                ? repoPath
                : Path.Combine(repoPath, subdirectory);

            if (!Directory.Exists(startPath))
            {
                return $"Error: Directory not found: {subdirectory ?? repositoryName}";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Directory tree for: {repositoryName}/{subdirectory ?? ""}");
            sb.AppendLine();

            BuildDirectoryTree(sb, startPath, "", 0, maxDepth);

            return sb.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting directory tree for: {Repository}", repositoryName);
            await Task.CompletedTask;
            return $"Error: {ex.Message}";
        }
    }

    [Description("Search for code patterns or text across files in the workspace")]
    public async Task<string> SearchCodeAsync(
        [Description("The regex pattern or text to search for")] string searchPattern,
        [Description("Optional: file pattern to filter (e.g., *.cs)")] string filePattern = "*",
        [Description("Optional: specific repository name, or all if not specified")] string? repositoryName = null)
    {
        try
        {
            var context = await _workspaceManager.ScanWorkspaceAsync();
            var results = new List<string>();
            var regex = new Regex(searchPattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);

            var repositoriesToSearch = string.IsNullOrWhiteSpace(repositoryName)
                ? context.Repositories.Values
                : context.Repositories.Where(r => r.Key == repositoryName).Select(r => r.Value);

            foreach (var repoInfo in repositoriesToSearch)
            {
                var files = Directory.GetFiles(repoInfo.Path, filePattern, SearchOption.AllDirectories)
                    .Where(f => !f.Contains("\\.git\\") && !f.Contains("\\bin\\") && !f.Contains("\\obj\\") && !f.Contains("\\node_modules\\"))
                    .Where(f => _securityService.IsAllowedFileType(f))
                    .Take(100); // Limit files scanned

                foreach (var file in files)
                {
                    try
                    {
                        var content = await File.ReadAllTextAsync(file);
                        var matches = regex.Matches(content);

                        if (matches.Count > 0)
                        {
                            var relativePath = Path.GetRelativePath(repoInfo.Path, file);
                            results.Add($"{repoInfo.Name}/{relativePath} ({matches.Count} matches)");

                            // Add first few match contexts
                            var lines = content.Split('\n');
                            foreach (Match match in matches.Take(3))
                            {
                                var lineNumber = content.Substring(0, match.Index).Count(c => c == '\n') + 1;
                                if (lineNumber > 0 && lineNumber <= lines.Length)
                                {
                                    results.Add($"  Line {lineNumber}: {lines[lineNumber - 1].Trim()}");
                                }
                            }

                            if (matches.Count > 3)
                            {
                                results.Add($"  ... and {matches.Count - 3} more matches");
                            }
                        }

                        if (results.Count > 50) break; // Limit total results
                    }
                    catch
                    {
                        // Skip files that can't be read
                    }
                }

                if (results.Count > 50) break;
            }

            _logger.LogInformation("Found {Count} results for pattern: {Pattern}", results.Count, searchPattern);

            return results.Count > 0
                ? string.Join("\n", results)
                : $"No matches found for pattern: {searchPattern}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching code with pattern: {Pattern}", searchPattern);
            return $"Error: {ex.Message}";
        }
    }

    [Description("Find class or interface definitions in code files")]
    public async Task<string> FindDefinitionsAsync(
        [Description("The name of the class, interface, or method to find")] string definitionName,
        [Description("Optional: specific repository name")] string? repositoryName = null)
    {
        try
        {
            // Search for class/interface definitions
            var classPattern = $@"(class|interface|enum|struct)\s+{Regex.Escape(definitionName)}\b";
            var methodPattern = $@"(public|private|protected|internal).*\s+{Regex.Escape(definitionName)}\s*\(";

            var results = new StringBuilder();
            results.AppendLine($"Searching for definitions of: {definitionName}");
            results.AppendLine();

            // Search for class definitions
            var classResults = await SearchCodeAsync(classPattern, "*.cs", repositoryName);
            if (!classResults.StartsWith("No matches"))
            {
                results.AppendLine("Class/Interface/Enum definitions:");
                results.AppendLine(classResults);
                results.AppendLine();
            }

            // Search for method definitions
            var methodResults = await SearchCodeAsync(methodPattern, "*.cs", repositoryName);
            if (!methodResults.StartsWith("No matches"))
            {
                results.AppendLine("Method definitions:");
                results.AppendLine(methodResults);
            }

            return results.Length > 50
                ? results.ToString()
                : $"No definitions found for: {definitionName}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding definitions: {Name}", definitionName);
            return $"Error: {ex.Message}";
        }
    }

    private void BuildDirectoryTree(StringBuilder sb, string path, string indent, int depth, int maxDepth)
    {
        if (depth >= maxDepth) return;

        try
        {
            var dirInfo = new DirectoryInfo(path);
            var directories = dirInfo.GetDirectories()
                .Where(d => d.Name != ".git" && d.Name != "bin" && d.Name != "obj" && d.Name != "node_modules")
                .OrderBy(d => d.Name)
                .ToList();

            var files = dirInfo.GetFiles()
                .OrderBy(f => f.Name)
                .ToList();

            // Show directories
            foreach (var dir in directories)
            {
                sb.AppendLine($"{indent}📁 {dir.Name}/");
                BuildDirectoryTree(sb, dir.FullName, indent + "  ", depth + 1, maxDepth);
            }

            // Show files
            foreach (var file in files.Take(20)) // Limit files per directory
            {
                sb.AppendLine($"{indent}📄 {file.Name}");
            }

            if (files.Count > 20)
            {
                sb.AppendLine($"{indent}... and {files.Count - 20} more files");
            }
        }
        catch
        {
            // Skip directories we can't access
        }
    }
}
