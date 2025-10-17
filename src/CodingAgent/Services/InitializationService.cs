namespace CodingAgent.Services;

public interface IInitializationService
{
    Task<WorkspaceContext> InitializeAsync();
}

public class InitializationService : IInitializationService
{
    private readonly IWorkspaceManager _workspaceManager;
    private readonly IGitService _gitService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<InitializationService> _logger;

    public InitializationService(
        IWorkspaceManager workspaceManager,
        IGitService gitService,
        IServiceProvider serviceProvider,
        ILogger<InitializationService> logger)
    {
        _workspaceManager = workspaceManager;
        _gitService = gitService;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<WorkspaceContext> InitializeAsync()
    {
        try
        {
            _logger.LogInformation("Starting agent initialization...");

            // Verify git is installed
            await VerifyGitInstalledAsync();

            // Initialize workspace
            await _workspaceManager.InitializeAsync();

            // Initialize Git service
            await _gitService.InitializeAsync();

            // Clone repositories
            var cloneResults = await _gitService.CloneAllRepositoriesAsync();
            foreach (var (repoName, success) in cloneResults)
            {
                if (success)
                {
                    _logger.LogInformation("Repository {RepoName} ready", repoName);
                }
                else
                {
                    _logger.LogError("Failed to clone repository {RepoName}", repoName);
                }
            }

            // Scan workspace and build context
            var context = await _workspaceManager.ScanWorkspaceAsync();
            _logger.LogInformation("Scanned {RepoCount} repositories", context.Repositories.Count);

            // Initialize session database
            // Create scope to resolve scoped ISessionStore (which depends on scoped DbContext)
            using var scope = _serviceProvider.CreateScope();
            var sessionStore = scope.ServiceProvider.GetRequiredService<ISessionStore>();
            await sessionStore.InitializeAsync();

            _logger.LogInformation("Agent initialization complete and ready");

            return context;
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Failed to initialize agent");
            throw;
        }
    }

    private async Task VerifyGitInstalledAsync()
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process == null)
            {
                throw new InvalidOperationException("Failed to start git process");
            }

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException("Git command returned non-zero exit code");
            }

            _logger.LogInformation("Git is installed: {Version}", output.Trim());
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Git is not installed or not available in PATH");
            throw new InvalidOperationException(
                "Git is required but not found. Please install Git and ensure it's in your system PATH.", ex);
        }
    }
}
