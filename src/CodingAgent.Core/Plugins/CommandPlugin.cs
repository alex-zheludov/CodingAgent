using CodingAgent.Services;
using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace CodingAgent.Plugins;

public class CommandPlugin
{
    private readonly ISecurityService _securityService;
    private readonly IWorkspaceManager _workspaceManager;
    private readonly ILogger<CommandPlugin> _logger;
    private const int DefaultTimeoutSeconds = 300; // 5 minutes

    public CommandPlugin(
        ISecurityService securityService,
        IWorkspaceManager workspaceManager,
        ILogger<CommandPlugin> logger)
    {
        _securityService = securityService;
        _workspaceManager = workspaceManager;
        _logger = logger;
    }

    [KernelFunction]
    [Description("Execute a whitelisted shell command in a repository directory")]
    public async Task<string> ExecuteCommandAsync(
        [Description("The command to execute (must be whitelisted)")] string command,
        [Description("The repository name to execute in")] string repositoryName,
        [Description("Optional: timeout in seconds (default 300)")] int timeoutSeconds = DefaultTimeoutSeconds)
    {
        try
        {
            // Validate command is whitelisted
            if (!_securityService.ValidateCommand(command, out var error))
            {
                _logger.LogWarning("Command validation failed: {Error}", error);
                return $"Error: {error}";
            }

            // Get repository path
            var repoPath = _workspaceManager.GetRepositoryPath(repositoryName);
            if (!Directory.Exists(repoPath))
            {
                return $"Error: Repository directory not found: {repositoryName}";
            }

            _logger.LogInformation("Executing command in {Repository}: {Command}", repositoryName, command);

            // Parse command (first part is executable, rest are arguments)
            var parts = command.Split(' ', 2, StringSplitOptions.TrimEntries);
            var executable = parts[0];
            var arguments = parts.Length > 1 ? parts[1] : string.Empty;

            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                WorkingDirectory = repoPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            using var process = new Process { StartInfo = startInfo };

            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data != null) outputBuilder.AppendLine(e.Data);
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data != null) errorBuilder.AppendLine(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var timeout = TimeSpan.FromSeconds(Math.Min(timeoutSeconds, 600)); // Max 10 minutes
            var completed = await process.WaitForExitAsync(timeout);

            if (!completed)
            {
                process.Kill();
                _logger.LogWarning("Command timed out after {Timeout} seconds", timeoutSeconds);
                return $"Error: Command timed out after {timeoutSeconds} seconds";
            }

            var exitCode = process.ExitCode;
            var output = outputBuilder.ToString();
            var errors = errorBuilder.ToString();

            var result = new StringBuilder();
            result.AppendLine($"Command: {command}");
            result.AppendLine($"Exit Code: {exitCode}");
            result.AppendLine();

            if (!string.IsNullOrWhiteSpace(output))
            {
                result.AppendLine("Output:");
                result.AppendLine(output);
            }

            if (!string.IsNullOrWhiteSpace(errors))
            {
                result.AppendLine("Errors:");
                result.AppendLine(errors);
            }

            if (exitCode == 0)
            {
                _logger.LogInformation("Command completed successfully in {Repository}", repositoryName);
            }
            else
            {
                _logger.LogWarning("Command failed with exit code {ExitCode} in {Repository}", exitCode, repositoryName);
            }

            return result.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing command: {Command}", command);
            return $"Error executing command: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("Run dotnet build in a repository")]
    public async Task<string> BuildDotnetAsync(
        [Description("The repository name to build")] string repositoryName)
    {
        return await ExecuteCommandAsync("dotnet build", repositoryName);
    }

    [KernelFunction]
    [Description("Run dotnet test in a repository")]
    public async Task<string> TestDotnetAsync(
        [Description("The repository name to test")] string repositoryName)
    {
        return await ExecuteCommandAsync("dotnet test", repositoryName);
    }

    [KernelFunction]
    [Description("Run npm install in a repository")]
    public async Task<string> NpmInstallAsync(
        [Description("The repository name")] string repositoryName)
    {
        return await ExecuteCommandAsync("npm install", repositoryName);
    }

    [KernelFunction]
    [Description("Run npm test in a repository")]
    public async Task<string> NpmTestAsync(
        [Description("The repository name")] string repositoryName)
    {
        return await ExecuteCommandAsync("npm test", repositoryName);
    }
}

// Extension method for timeout
public static class ProcessExtensions
{
    public static async Task<bool> WaitForExitAsync(this Process process, TimeSpan timeout)
    {
        return await Task.Run(() => process.WaitForExit(timeout));
    }
}
