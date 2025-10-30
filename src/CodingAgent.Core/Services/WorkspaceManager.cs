using CodingAgent.Configuration;
using Microsoft.Extensions.Options;

namespace CodingAgent.Services;

public interface IWorkspaceManager
{
    Task InitializeAsync();
    string GetWorkspacePath();
    string GetRepositoryPath(string repositoryName);
    string GetSessionDataPath();
    string ValidateAndNormalizePath(string path);
    bool IsPathWithinWorkspace(string path);
    Task<WorkspaceContext> ScanWorkspaceAsync();
}

public class WorkspaceContext
{
    public Dictionary<string, RepositoryInfo> Repositories { get; set; } = new();
    public DateTime ScanTime { get; set; }
}

public class RepositoryInfo
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public int TotalFiles { get; set; }
    public Dictionary<string, int> FilesByExtension { get; set; } = new();
    public List<string> KeyFiles { get; set; } = new();
}

public class WorkspaceManager : IWorkspaceManager
{
    private readonly AgentSettings _agentSettings;
    private readonly ILogger<WorkspaceManager> _logger;
    private readonly string _workspaceRoot;
    private readonly string _sessionDataPath;

    public WorkspaceManager(
        IOptions<AgentSettings> agentSettings,
        ILogger<WorkspaceManager> logger)
    {
        _agentSettings = agentSettings.Value;
        _logger = logger;
        _workspaceRoot = Path.GetFullPath(_agentSettings.Workspace.Root);
        _sessionDataPath = Path.GetFullPath(_agentSettings.Session.Root);
    }

    public async Task InitializeAsync()
    {
        _logger.LogInformation("Initializing workspace at {WorkspaceRoot}", _workspaceRoot);

        // Create workspace root if it doesn't exist
        if (!Directory.Exists(_workspaceRoot))
        {
            Directory.CreateDirectory(_workspaceRoot);
            _logger.LogInformation("Created workspace root directory");
        }

        // Create session data directory
        if (!Directory.Exists(_sessionDataPath))
        {
            Directory.CreateDirectory(_sessionDataPath);
            _logger.LogInformation("Created session data directory");
        }

        await Task.CompletedTask;
    }

    public string GetWorkspacePath()
    {
        return _workspaceRoot;
    }

    public string GetRepositoryPath(string repositoryName)
    {
        // Check if this repository has a LocalPath configured
        var repoConfig = _agentSettings.Repositories.FirstOrDefault(r => r.Name == repositoryName);
        if (repoConfig?.LocalPath != null)
        {
            return Path.GetFullPath(repoConfig.LocalPath);
        }

        var repoPath = Path.Combine(_workspaceRoot, repositoryName);
        return Path.GetFullPath(repoPath);
    }

    public string GetSessionDataPath()
    {
        return _sessionDataPath;
    }

    public string ValidateAndNormalizePath(string path)
    {
        // Normalize the path
        var normalizedPath = Path.GetFullPath(path);

        // Check if it's within workspace
        if (!IsPathWithinWorkspace(normalizedPath))
        {
            throw new UnauthorizedAccessException($"Path is outside workspace: {path}");
        }

        return normalizedPath;
    }

    public bool IsPathWithinWorkspace(string path)
    {
        var normalizedPath = Path.GetFullPath(path);
        return normalizedPath.StartsWith(_workspaceRoot, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<WorkspaceContext> ScanWorkspaceAsync()
    {
        _logger.LogInformation("Scanning workspace");

        var context = new WorkspaceContext
        {
            ScanTime = DateTime.UtcNow
        };

        foreach (var repo in _agentSettings.Repositories)
        {
            var repoPath = GetRepositoryPath(repo.Name);

            if (!Directory.Exists(repoPath))
            {
                _logger.LogWarning("Repository directory not found: {RepoPath}", repoPath);
                continue;
            }

            var repoInfo = await ScanRepositoryAsync(repo.Name, repoPath);
            context.Repositories[repo.Name] = repoInfo;
        }

        return context;
    }

    private async Task<RepositoryInfo> ScanRepositoryAsync(string name, string path)
    {
        var info = new RepositoryInfo
        {
            Name = name,
            Path = path
        };

        var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories)
            .Where(f => !f.Contains("\\.git\\") && !f.Contains("\\bin\\") && !f.Contains("\\obj\\") && !f.Contains("\\node_modules\\"))
            .ToList();

        info.TotalFiles = files.Count;

        // Count files by extension
        foreach (var file in files)
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (string.IsNullOrEmpty(ext)) ext = "(no extension)";

            if (!info.FilesByExtension.ContainsKey(ext))
                info.FilesByExtension[ext] = 0;

            info.FilesByExtension[ext]++;
        }

        // Find key files
        var keyFileNames = new[] { "README.md", "README.txt", "package.json", ".csproj", ".sln", "appsettings.json", "Program.cs" };
        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            if (keyFileNames.Any(k => fileName.EndsWith(k, StringComparison.OrdinalIgnoreCase)))
            {
                info.KeyFiles.Add(file.Replace(path, "").TrimStart('\\', '/'));
            }
        }

        await Task.CompletedTask;
        return info;
    }
}
