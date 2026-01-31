using System.Text.RegularExpressions;
using CodingAgent.Core.Configuration;

namespace CodingAgent.Core.Services;

public interface ISecurityService
{
    bool ValidateFilePath(string path, out string? error);
    bool ValidateFileSize(long sizeBytes, out string? error);
    bool ValidateCommand(string command, out string? error);
    bool IsAllowedFileType(string path);
}

public class SecurityService : ISecurityService
{
    private readonly SecuritySettings _settings;
    private readonly IWorkspaceManager _workspaceManager;
    private readonly ILogger<SecurityService> _logger;

    private static readonly string[] BinaryExtensions = new[]
    {
        ".exe", ".dll", ".so", ".dylib", ".bin", ".dat",
        ".zip", ".tar", ".gz", ".7z", ".rar",
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".ico",
        ".mp3", ".mp4", ".avi", ".mov", ".wav"
    };

    private static readonly string[] DangerousCommandPatterns = new[]
    {
        @"rm\s+-rf",
        @"del\s+/f",
        @"format",
        @"mkfs",
        @"dd\s+if=",
        @"curl",
        @"wget",
        @"nc\s+",
        @"netcat",
        @"sudo",
        @"chmod\s+\+x",
        @"&&",
        @"\|\|",
        @";",
        @"\|"
    };

    public SecurityService(
        IOptions<SecuritySettings> settings,
        IWorkspaceManager workspaceManager,
        ILogger<SecurityService> logger)
    {
        _settings = settings.Value;
        _workspaceManager = workspaceManager;
        _logger = logger;
    }

    public bool ValidateFilePath(string path, out string? error)
    {
        error = null;

        try
        {
            // Normalize and check if within workspace
            var normalizedPath = Path.GetFullPath(path);

            if (!_workspaceManager.IsPathWithinWorkspace(normalizedPath))
            {
                error = $"Path is outside workspace: {path}";
                _logger.LogWarning("Security violation: {Error}", error);
                return false;
            }

            // Check for path traversal attempts
            if (path.Contains(".."))
            {
                error = "Path traversal detected";
                _logger.LogWarning("Security violation: Path traversal in {Path}", path);
                return false;
            }

            // Check for system directories (Unix)
            if (!OperatingSystem.IsWindows())
            {
                var dangerousPaths = new[] { "/etc/", "/sys/", "/proc/", "/dev/", "/root/" };
                if (dangerousPaths.Any(d => normalizedPath.StartsWith(d)))
                {
                    error = "Access to system directories is not allowed";
                    _logger.LogWarning("Security violation: System directory access attempt: {Path}", normalizedPath);
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"Invalid path: {ex.Message}";
            return false;
        }
    }

    public bool ValidateFileSize(long sizeBytes, out string? error)
    {
        error = null;

        if (sizeBytes > _settings.FileSizeLimitBytes)
        {
            error = $"File size {sizeBytes} bytes exceeds limit of {_settings.FileSizeLimitMB}MB";
            _logger.LogWarning("Security violation: {Error}", error);
            return false;
        }

        return true;
    }

    public bool ValidateCommand(string command, out string? error)
    {
        error = null;

        // Check for dangerous patterns
        foreach (var pattern in DangerousCommandPatterns)
        {
            if (Regex.IsMatch(command, pattern, RegexOptions.IgnoreCase))
            {
                error = $"Command contains dangerous pattern: {pattern}";
                _logger.LogWarning("Security violation: {Error} in command: {Command}", error, command);
                return false;
            }
        }

        // Check if command starts with any whitelisted command
        var isWhitelisted = _settings.AllowedCommands.Any(allowed =>
            command.Equals(allowed, StringComparison.OrdinalIgnoreCase) ||
            command.StartsWith(allowed + " ", StringComparison.OrdinalIgnoreCase));

        if (!isWhitelisted)
        {
            error = $"Command is not whitelisted: {command}";
            _logger.LogWarning("Security violation: {Error}", error);
            return false;
        }

        return true;
    }

    public bool IsAllowedFileType(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        var isBinary = BinaryExtensions.Contains(extension);

        if (isBinary)
        {
            _logger.LogWarning("Binary file type rejected: {Path}", path);
        }

        return !isBinary;
    }
}
