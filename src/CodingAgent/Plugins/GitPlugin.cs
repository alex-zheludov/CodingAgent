using CodingAgent.Services;
using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text;

namespace CodingAgent.Plugins;

public class GitPlugin
{
    private readonly IGitService _gitService;
    private readonly ILogger<GitPlugin> _logger;

    public GitPlugin(IGitService gitService, ILogger<GitPlugin> logger)
    {
        _gitService = gitService;
        _logger = logger;
    }

    [KernelFunction]
    [Description("Get the status of a Git repository showing modified, staged, and untracked files")]
    public async Task<string> GetStatusAsync(
        [Description("The name of the repository")] string repositoryName)
    {
        try
        {
            var status = await _gitService.GetStatusAsync(repositoryName);

            var sb = new StringBuilder();
            sb.AppendLine($"Repository: {status.RepositoryName}");
            sb.AppendLine($"Current Branch: {status.CurrentBranch}");
            sb.AppendLine();

            if (status.ModifiedFiles.Count > 0)
            {
                sb.AppendLine("Modified files:");
                foreach (var file in status.ModifiedFiles)
                {
                    sb.AppendLine($"  M {file}");
                }
                sb.AppendLine();
            }

            if (status.StagedFiles.Count > 0)
            {
                sb.AppendLine("Staged files:");
                foreach (var file in status.StagedFiles)
                {
                    sb.AppendLine($"  A {file}");
                }
                sb.AppendLine();
            }

            if (status.UntrackedFiles.Count > 0)
            {
                sb.AppendLine("Untracked files:");
                foreach (var file in status.UntrackedFiles)
                {
                    sb.AppendLine($"  ? {file}");
                }
            }

            if (status.ModifiedFiles.Count == 0 && status.StagedFiles.Count == 0 && status.UntrackedFiles.Count == 0)
            {
                sb.AppendLine("Working directory clean");
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Git status for: {Repository}", repositoryName);
            return $"Error: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("Get the diff of changes in a repository")]
    public async Task<string> GetDiffAsync(
        [Description("The name of the repository")] string repositoryName,
        [Description("Optional: specific file path to show diff for")] string? filePath = null)
    {
        try
        {
            var diff = await _gitService.GetDiffAsync(repositoryName, filePath);
            _logger.LogInformation("Got diff for repository: {Repository}", repositoryName);
            return string.IsNullOrEmpty(diff) ? "No changes to show" : diff;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting diff for: {Repository}", repositoryName);
            return $"Error: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("Stage files for commit in a repository")]
    public async Task<string> StageFilesAsync(
        [Description("The name of the repository")] string repositoryName,
        [Description("Comma-separated list of file paths to stage")] string filePaths)
    {
        try
        {
            var files = filePaths.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            await _gitService.StageFilesAsync(repositoryName, files);
            _logger.LogInformation("Staged {Count} files in {Repository}", files.Length, repositoryName);
            return $"Staged {files.Length} files successfully";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error staging files in: {Repository}", repositoryName);
            return $"Error: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("Create a commit with staged changes")]
    public async Task<string> CommitAsync(
        [Description("The name of the repository")] string repositoryName,
        [Description("The commit message")] string message)
    {
        try
        {
            var sha = await _gitService.CommitAsync(repositoryName, message);
            _logger.LogInformation("Created commit {Sha} in {Repository}", sha[..7], repositoryName);
            return $"Created commit: {sha[..7]}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error committing in: {Repository}", repositoryName);
            return $"Error: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("Create and checkout a new branch")]
    public async Task<string> CreateBranchAsync(
        [Description("The name of the repository")] string repositoryName,
        [Description("The branch name (prefix will be added automatically)")] string branchName)
    {
        try
        {
            await _gitService.CreateBranchAsync(repositoryName, branchName);
            _logger.LogInformation("Created branch in {Repository}", repositoryName);
            return $"Created and checked out new branch";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating branch in: {Repository}", repositoryName);
            return $"Error: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("Push commits to remote repository")]
    public async Task<string> PushAsync(
        [Description("The name of the repository")] string repositoryName)
    {
        try
        {
            await _gitService.PushAsync(repositoryName);
            _logger.LogInformation("Pushed changes in {Repository}", repositoryName);
            return "Successfully pushed changes to remote";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pushing in: {Repository}", repositoryName);
            return $"Error: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("Get recent commit history")]
    public async Task<string> GetCommitHistoryAsync(
        [Description("The name of the repository")] string repositoryName,
        [Description("Number of commits to retrieve (default 10)")] int count = 10)
    {
        try
        {
            var commits = await _gitService.GetCommitHistoryAsync(repositoryName, count);

            var sb = new StringBuilder();
            foreach (var commit in commits)
            {
                sb.AppendLine($"{commit.Sha[..7]} - {commit.Message}");
                sb.AppendLine($"  Author: {commit.Author} | Date: {commit.Date:yyyy-MM-dd HH:mm}");
                sb.AppendLine();
            }

            return commits.Count > 0 ? sb.ToString() : "No commits found";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting commit history for: {Repository}", repositoryName);
            return $"Error: {ex.Message}";
        }
    }
}
