using System.ComponentModel;
using CodingAgent.Core.Services;

namespace CodingAgent.Core.Plugins;

public interface IFileOperationsPlugin
{
    Task<string> ReadFileAsync(
        [Description("The path to the file relative to workspace root")] string filePath);

    Task<string> WriteFileAsync(
        [Description("The path to the file relative to workspace root")] string filePath,
        [Description("The content to write to the file")] string content);

    Task<string> ListDirectoryAsync(
        [Description("The directory path relative to workspace root")] string directoryPath,
        [Description("File pattern to match (e.g., *.cs)")] string pattern = "*");

    Task<string> FindFilesAsync(
        [Description("File name pattern to search for (e.g., *.cs, Program.cs)")] string pattern);
}

public class FileOperationsPlugin : IFileOperationsPlugin
{
    private readonly IWorkspaceManager _workspaceManager;
    private readonly ISecurityService _securityService;
    private readonly ILogger<FileOperationsPlugin> _logger;

    public FileOperationsPlugin(
        IWorkspaceManager workspaceManager,
        ISecurityService securityService,
        ILogger<FileOperationsPlugin> logger)
    {
        _workspaceManager = workspaceManager;
        _securityService = securityService;
        _logger = logger;
    }

    [Description("Read the contents of a file from the workspace")]
    public async Task<string> ReadFileAsync(
        [Description("The path to the file relative to workspace root")] string filePath)
    {
        try
        {
            var fullPath = Path.Combine(_workspaceManager.GetWorkspacePath(), filePath);
            var normalizedPath = _workspaceManager.ValidateAndNormalizePath(fullPath);

            if (!_securityService.ValidateFilePath(normalizedPath, out var error))
            {
                return $"Error: {error}";
            }

            if (!File.Exists(normalizedPath))
            {
                return $"Error: File not found: {filePath}";
            }

            var fileInfo = new FileInfo(normalizedPath);
            if (!_securityService.ValidateFileSize(fileInfo.Length, out error))
            {
                return $"Error: {error}";
            }

            if (!_securityService.IsAllowedFileType(normalizedPath))
            {
                return $"Error: Binary file operations not allowed: {filePath}";
            }

            var content = await File.ReadAllTextAsync(normalizedPath);
            _logger.LogInformation("Read file: {FilePath} ({Length} bytes)", filePath, content.Length);

            return content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading file: {FilePath}", filePath);
            return $"Error reading file: {ex.Message}";
        }
    }

    [Description("Write content to a file in the workspace")]
    public async Task<string> WriteFileAsync(
        [Description("The path to the file relative to workspace root")] string filePath,
        [Description("The content to write to the file")] string content)
    {
        try
        {
            var fullPath = Path.Combine(_workspaceManager.GetWorkspacePath(), filePath);
            var normalizedPath = _workspaceManager.ValidateAndNormalizePath(fullPath);

            if (!_securityService.ValidateFilePath(normalizedPath, out var error))
            {
                return $"Error: {error}";
            }

            if (!_securityService.ValidateFileSize(content.Length, out error))
            {
                return $"Error: {error}";
            }

            if (!_securityService.IsAllowedFileType(normalizedPath))
            {
                return $"Error: Binary file operations not allowed: {filePath}";
            }

            // Create directory if it doesn't exist
            var directory = Path.GetDirectoryName(normalizedPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(normalizedPath, content);
            _logger.LogInformation("Wrote file: {FilePath} ({Length} bytes)", filePath, content.Length);

            return $"Successfully wrote {content.Length} bytes to {filePath}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing file: {FilePath}", filePath);
            return $"Error writing file: {ex.Message}";
        }
    }

    [Description("List files and directories in a directory")]
    public async Task<string> ListDirectoryAsync(
        [Description("The directory path relative to workspace root")] string directoryPath,
        [Description("File pattern to match (e.g., *.cs)")] string pattern = "*")
    {
        try
        {
            var fullPath = Path.Combine(_workspaceManager.GetWorkspacePath(), directoryPath);
            var normalizedPath = _workspaceManager.ValidateAndNormalizePath(fullPath);

            if (!_securityService.ValidateFilePath(normalizedPath, out var error))
            {
                return $"Error: {error}";
            }

            if (!Directory.Exists(normalizedPath))
            {
                return $"Error: Directory not found: {directoryPath}";
            }

            var results = new List<string>();

            // Get directories first
            var directories = Directory.GetDirectories(normalizedPath)
                .Select(d => Path.GetFileName(d) + "/")
                .OrderBy(d => d)
                .ToList();

            // Get files matching pattern
            var files = Directory.GetFiles(normalizedPath, pattern, SearchOption.TopDirectoryOnly)
                .Select(f => Path.GetFileName(f))
                .OrderBy(f => f)
                .ToList();

            results.AddRange(directories);
            results.AddRange(files);

            _logger.LogInformation("Listed {DirectoryCount} directories and {FileCount} files in {Directory}",
                directories.Count, files.Count, directoryPath);

            return results.Count > 0
                ? string.Join("\n", results)
                : "(empty directory)";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing files in: {Directory}", directoryPath);
            await Task.CompletedTask;
            return $"Error listing files: {ex.Message}";
        }
    }

    [Description("Search for files by name pattern across the workspace")]
    public async Task<string> FindFilesAsync(
        [Description("File name pattern to search for (e.g., *.cs, Program.cs)")] string pattern)
    {
        try
        {
            var workspaceContext = await _workspaceManager.ScanWorkspaceAsync();
            var results = new List<string>();

            foreach (var (repoName, repoInfo) in workspaceContext.Repositories)
            {
                var files = Directory.GetFiles(repoInfo.Path, pattern, SearchOption.AllDirectories)
                    .Where(f => !f.Contains("\\.git\\") && !f.Contains("\\bin\\") && !f.Contains("\\obj\\"))
                    .Take(50) // Limit results
                    .ToList();

                foreach (var file in files)
                {
                    var relativePath = Path.GetRelativePath(repoInfo.Path, file);
                    results.Add($"{repoName}/{relativePath}");
                }
            }

            _logger.LogInformation("Found {Count} files matching pattern: {Pattern}", results.Count, pattern);

            return results.Count > 0
                ? string.Join("\n", results)
                : $"No files found matching pattern: {pattern}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding files with pattern: {Pattern}", pattern);
            return $"Error finding files: {ex.Message}";
        }
    }
}
