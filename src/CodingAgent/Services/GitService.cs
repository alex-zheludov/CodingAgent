using CodingAgent.Configuration;
using LibGit2Sharp;
using Microsoft.Extensions.Options;
using System.Text;

namespace CodingAgent.Services;

public interface IGitService
{
    Task InitializeAsync();
    Task<Dictionary<string, bool>> CloneAllRepositoriesAsync();
    Task<bool> CloneRepositoryAsync(string name, string url, string branch);
    Task<GitStatus> GetStatusAsync(string repositoryName);
    Task<string> GetDiffAsync(string repositoryName, string? filePath = null);
    Task StageFilesAsync(string repositoryName, params string[] filePaths);
    Task<string> CommitAsync(string repositoryName, string message);
    Task CreateBranchAsync(string repositoryName, string branchName);
    Task CheckoutBranchAsync(string repositoryName, string branchName);
    Task PushAsync(string repositoryName, string? remoteName = null);
    Task<List<GitCommit>> GetCommitHistoryAsync(string repositoryName, int count = 10);
}

public class GitStatus
{
    public string RepositoryName { get; set; } = string.Empty;
    public string CurrentBranch { get; set; } = string.Empty;
    public List<string> ModifiedFiles { get; set; } = new();
    public List<string> StagedFiles { get; set; } = new();
    public List<string> UntrackedFiles { get; set; } = new();
}

public class GitCommit
{
    public string Sha { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public DateTime Date { get; set; }
}

public class GitService : IGitService
{
    private readonly AgentSettings _agentSettings;
    private readonly GitSettings _gitSettings;
    private readonly IWorkspaceManager _workspaceManager;
    private readonly ILogger<GitService> _logger;
    private string? _sshKeyPath;

    public GitService(
        IOptions<AgentSettings> agentSettings,
        IOptions<GitSettings> gitSettings,
        IWorkspaceManager workspaceManager,
        ILogger<GitService> logger)
    {
        _agentSettings = agentSettings.Value;
        _gitSettings = gitSettings.Value;
        _workspaceManager = workspaceManager;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        _logger.LogInformation("Initializing Git service");

        // Setup SSH key
        if (!string.IsNullOrWhiteSpace(_gitSettings.SshKeyPath))
        {
            _sshKeyPath = _gitSettings.SshKeyPath;
            if (!File.Exists(_sshKeyPath))
            {
                throw new FileNotFoundException($"SSH key file not found: {_sshKeyPath}");
            }
            _logger.LogInformation("Using SSH key from path: {SshKeyPath}", _sshKeyPath);
        }
        else if (!string.IsNullOrWhiteSpace(_gitSettings.SshKeyBase64))
        {
            // Decode base64 and save to temp file
            var keyBytes = Convert.FromBase64String(_gitSettings.SshKeyBase64);
            var sessionPath = _workspaceManager.GetSessionDataPath();
            _sshKeyPath = Path.Combine(sessionPath, "id_rsa");
            await File.WriteAllBytesAsync(_sshKeyPath, keyBytes);

            // Set permissions (Unix only)
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(_sshKeyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            _logger.LogInformation("SSH key decoded and saved to session directory");
        }

        await Task.CompletedTask;
    }

    public async Task<Dictionary<string, bool>> CloneAllRepositoriesAsync()
    {
        var results = new Dictionary<string, bool>();

        foreach (var repo in _agentSettings.Repositories)
        {
            var success = await CloneRepositoryAsync(repo.Name, repo.Url, repo.Branch);
            results[repo.Name] = success;
        }

        return results;
    }

    public async Task<bool> CloneRepositoryAsync(string name, string url, string branch)
    {
        try
        {
            var repoPath = _workspaceManager.GetRepositoryPath(name);

            if (Directory.Exists(repoPath))
            {
                _logger.LogInformation("Repository {Name} already exists at {Path}", name, repoPath);
                return true;
            }

            _logger.LogInformation("Cloning repository {Name} from {Url}", name, url);

            // Use system git command instead of LibGit2Sharp for cloning (better SSH support)
            var success = await CloneUsingGitCommandAsync(url, repoPath, branch);

            if (success)
            {
                _logger.LogInformation("Successfully cloned repository {Name}", name);
            }
            else
            {
                _logger.LogError("Failed to clone repository {Name}", name);
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clone repository {Name}", name);
            return false;
        }
    }

    private async Task<bool> CloneUsingGitCommandAsync(string url, string targetPath, string branch)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"clone --branch {branch} {url} \"{targetPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            _logger.LogDebug("Executing: git {Arguments}", startInfo.Arguments);

            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process == null)
            {
                _logger.LogError("Failed to start git process");
                return false;
            }

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                _logger.LogDebug("Git clone output: {Output}", output);
                return true;
            }
            else
            {
                _logger.LogError("Git clone failed with exit code {ExitCode}. Error: {Error}",
                    process.ExitCode, error);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during git clone");
            return false;
        }
    }

    public async Task<GitStatus> GetStatusAsync(string repositoryName)
    {
        var repoPath = _workspaceManager.GetRepositoryPath(repositoryName);
        using var repo = new Repository(repoPath);

        var status = new GitStatus
        {
            RepositoryName = repositoryName,
            CurrentBranch = repo.Head.FriendlyName
        };

        foreach (var item in repo.RetrieveStatus())
        {
            if (item.State.HasFlag(FileStatus.ModifiedInWorkdir) || item.State.HasFlag(FileStatus.ModifiedInIndex))
            {
                status.ModifiedFiles.Add(item.FilePath);
            }

            if (item.State.HasFlag(FileStatus.NewInIndex))
            {
                status.StagedFiles.Add(item.FilePath);
            }

            if (item.State.HasFlag(FileStatus.NewInWorkdir))
            {
                status.UntrackedFiles.Add(item.FilePath);
            }
        }

        await Task.CompletedTask;
        return status;
    }

    public async Task<string> GetDiffAsync(string repositoryName, string? filePath = null)
    {
        var repoPath = _workspaceManager.GetRepositoryPath(repositoryName);
        using var repo = new Repository(repoPath);

        var diff = repo.Diff.Compare<Patch>();
        var result = new StringBuilder();

        foreach (var patch in diff)
        {
            if (filePath == null || patch.Path == filePath)
            {
                result.AppendLine($"--- {patch.Path}");
                result.AppendLine(patch.Patch);
            }
        }

        await Task.CompletedTask;
        return result.ToString();
    }

    public async Task StageFilesAsync(string repositoryName, params string[] filePaths)
    {
        var repoPath = _workspaceManager.GetRepositoryPath(repositoryName);
        using var repo = new Repository(repoPath);

        Commands.Stage(repo, filePaths);

        _logger.LogInformation("Staged {Count} files in {Repository}", filePaths.Length, repositoryName);
        await Task.CompletedTask;
    }

    public async Task<string> CommitAsync(string repositoryName, string message)
    {
        var repoPath = _workspaceManager.GetRepositoryPath(repositoryName);
        using var repo = new Repository(repoPath);

        var signature = new Signature(_gitSettings.CommitAuthorName, _gitSettings.CommitAuthorEmail, DateTimeOffset.Now);
        var commit = repo.Commit(message, signature, signature);

        _logger.LogInformation("Created commit {Sha} in {Repository}", commit.Sha[..7], repositoryName);
        await Task.CompletedTask;
        return commit.Sha;
    }

    public async Task CreateBranchAsync(string repositoryName, string branchName)
    {
        var repoPath = _workspaceManager.GetRepositoryPath(repositoryName);
        using var repo = new Repository(repoPath);

        var fullBranchName = $"{_gitSettings.TargetBranchPrefix}{branchName}";
        var branch = repo.CreateBranch(fullBranchName);
        Commands.Checkout(repo, branch);

        _logger.LogInformation("Created and checked out branch {Branch} in {Repository}", fullBranchName, repositoryName);
        await Task.CompletedTask;
    }

    public async Task CheckoutBranchAsync(string repositoryName, string branchName)
    {
        var repoPath = _workspaceManager.GetRepositoryPath(repositoryName);
        using var repo = new Repository(repoPath);

        Commands.Checkout(repo, branchName);

        _logger.LogInformation("Checked out branch {Branch} in {Repository}", branchName, repositoryName);
        await Task.CompletedTask;
    }

    public async Task PushAsync(string repositoryName, string? remoteName = null)
    {
        var repoPath = _workspaceManager.GetRepositoryPath(repositoryName);
        using var repo = new Repository(repoPath);

        var remote = repo.Network.Remotes[remoteName ?? "origin"];
        var branch = repo.Head;

        var pushOptions = new PushOptions
        {
            CredentialsProvider = GetCredentialsProvider()
        };

        repo.Network.Push(remote, branch.CanonicalName, pushOptions);

        _logger.LogInformation("Pushed branch {Branch} to {Remote} in {Repository}", branch.FriendlyName, remote.Name, repositoryName);
        await Task.CompletedTask;
    }

    public async Task<List<GitCommit>> GetCommitHistoryAsync(string repositoryName, int count = 10)
    {
        var repoPath = _workspaceManager.GetRepositoryPath(repositoryName);
        using var repo = new Repository(repoPath);

        var commits = repo.Commits
            .Take(count)
            .Select(c => new GitCommit
            {
                Sha = c.Sha,
                Message = c.MessageShort,
                Author = c.Author.Name,
                Date = c.Author.When.UtcDateTime
            })
            .ToList();

        await Task.CompletedTask;
        return commits;
    }

    private LibGit2Sharp.Handlers.CredentialsHandler GetCredentialsProvider()
    {
        return (url, usernameFromUrl, types) =>
        {
            if (_sshKeyPath == null)
            {
                throw new InvalidOperationException("SSH key not configured");
            }

            _logger.LogDebug("Providing SSH credentials for {Url} with key {KeyPath}", url, _sshKeyPath);

            // Check if credential types include SSH
            if (types.HasFlag(SupportedCredentialTypes.UsernamePassword))
            {
                // For HTTPS URLs (fallback)
                return new UsernamePasswordCredentials
                {
                    Username = usernameFromUrl ?? "git",
                    Password = string.Empty
                };
            }

            // For SSH URLs - use default credentials which will attempt SSH agent first
            // LibGit2Sharp will use the system's SSH agent or fall back to SSH keys in ~/.ssh/
            return new DefaultCredentials();
        };
    }
}
