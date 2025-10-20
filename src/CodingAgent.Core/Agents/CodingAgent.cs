using CodingAgent.Configuration;
using CodingAgent.Models;
using CodingAgent.Plugins;
using CodingAgent.Services;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Polly;
using Polly.Retry;

namespace CodingAgent.Agents;

public interface ICodingAgent
{
    string SessionId { get; }
    Task ExecuteInstructionAsync(string instruction);
    Task SendMessageAsync(string message);
    Task<AgentStatus> GetStatusAsync();
    Task<HealthCheckResponse> GetHealthAsync();
    Task StopAsync();
}

public class CodingCodingAgent : ICodingAgent
{
    private readonly Kernel _kernel;
    private readonly IChatCompletionService _chatService;
    private readonly AgentSettings _agentSettings;
    private readonly AzureOpenAISettings _azureOpenAISettings;
    private readonly ISessionStore _sessionStore;
    private readonly IGitService _gitService;
    private readonly IWorkspaceManager _workspaceManager;
    private readonly ILogger<CodingCodingAgent> _logger;
    private readonly WorkspaceContext _workspaceContext;
    private AgentState _currentState = AgentState.Idle;
    private string _currentActivity = string.Empty;
    private readonly DateTimeOffset _startTime = DateTimeOffset.UtcNow;
    private readonly ChatHistory _chatHistory = new();
    private readonly SemaphoreSlim _executionLock = new(1, 1);
    private readonly PromptExecutionSettings _executionSettings;
    private readonly ResiliencePipeline _resiliencePipeline;

    public string SessionId { get; }

    public CodingCodingAgent(
        IOptions<AgentSettings> agentSettings,
        IOptions<AzureOpenAISettings> azureOpenAISettings,
        ISessionStore sessionStore,
        IGitService gitService,
        IWorkspaceManager workspaceManager,
        WorkspaceContext workspaceContext,
        ILogger<CodingCodingAgent> logger,
        IServiceProvider serviceProvider)
    {
        _agentSettings = agentSettings.Value;
        _azureOpenAISettings = azureOpenAISettings.Value;
        _sessionStore = sessionStore;
        _gitService = gitService;
        _workspaceManager = workspaceManager;
        _workspaceContext = workspaceContext;
        _logger = logger;
        SessionId = _agentSettings.Session.SessionId;

        // Build the kernel with Azure OpenAI using built-in connector
        var kernelBuilder = Kernel.CreateBuilder();

        // Add Azure OpenAI chat completion service directly
        kernelBuilder.AddAzureOpenAIChatCompletion(
            deploymentName: _azureOpenAISettings.Model,
            endpoint: _azureOpenAISettings.Endpoint,
            apiKey: _azureOpenAISettings.ApiKey);

        // Add plugins
        kernelBuilder.Plugins.AddFromObject(
            serviceProvider.GetRequiredService<FileOpsPlugin>(),
            "FileOps");

        kernelBuilder.Plugins.AddFromObject(
            serviceProvider.GetRequiredService<GitPlugin>(),
            "Git");

        kernelBuilder.Plugins.AddFromObject(
            serviceProvider.GetRequiredService<CommandPlugin>(),
            "Command");

        kernelBuilder.Plugins.AddFromObject(
            serviceProvider.GetRequiredService<CodeNavPlugin>(),
            "CodeNav");

        _kernel = kernelBuilder.Build();

        // Get the chat completion service from the kernel
        _chatService = _kernel.GetRequiredService<IChatCompletionService>();

        // Configure execution settings with model and parameters from AzureOpenAISettings
        // Enable automatic function calling
        _executionSettings = new OpenAIPromptExecutionSettings
        {
            ModelId = _azureOpenAISettings.Model,
            MaxTokens = _azureOpenAISettings.MaxTokens,
            Temperature = (double)_azureOpenAISettings.Temperature,
            ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
        };

        _logger.LogInformation(
            "Agent initialized with model: {Model}, MaxTokens: {MaxTokens}, Temperature: {Temperature}",
            _azureOpenAISettings.Model,
            _azureOpenAISettings.MaxTokens,
            _azureOpenAISettings.Temperature);

        // Configure Polly resilience pipeline for Azure OpenAI rate limiting
        _resiliencePipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>(ex =>
                    ex.Message.Contains("429") || ex.Message.Contains("rate limit")),
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(2),
                BackoffType = DelayBackoffType.Exponential,
                OnRetry = args =>
                {
                    _logger.LogWarning(
                        "Rate limit hit (HTTP 429). Retrying in {Delay} seconds... (Attempt {AttemptNumber})",
                        args.RetryDelay.TotalSeconds,
                        args.AttemptNumber);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();

        // Initialize system message
        InitializeSystemMessage();
    }

    private void InitializeSystemMessage()
    {
        var systemPrompt = BuildSystemPrompt();
        _chatHistory.AddSystemMessage(systemPrompt);
    }

    private string BuildContextInfo()
    {
        var contextLines = new List<string>();

        foreach (var (repoName, repoInfo) in _workspaceContext.Repositories)
        {
            contextLines.Add($"  Repository: {repoName}");
            contextLines.Add($"    - Path: {repoInfo.Path}");
            contextLines.Add($"    - Total Files: {repoInfo.TotalFiles}");

            if (repoInfo.FilesByExtension.Any())
            {
                contextLines.Add("    - File Types:");
                foreach (var (ext, count) in repoInfo.FilesByExtension.OrderByDescending(x => x.Value).Take(10))
                {
                    contextLines.Add($"      {ext}: {count} files");
                }
            }

            if (repoInfo.KeyFiles.Any())
            {
                contextLines.Add("    - Key Files:");
                foreach (var keyFile in repoInfo.KeyFiles.Take(10))
                {
                    contextLines.Add($"      - {keyFile}");
                }
            }

            contextLines.Add("");
        }

        return string.Join("\n", contextLines);
    }

    private string BuildSystemPrompt()
    {
        var contextInfo = BuildContextInfo();

        return $"""
            You are an autonomous coding agent working on software development tasks.

            ## Your Environment
            - Session ID: {SessionId}
            - Workspace Context:
            {contextInfo}

            ## IMPORTANT: File Path Structure
            - All file paths are relative to the workspace root
            - Each repository is a subdirectory in the workspace
            - To access files in a repository, use paths like: "RepositoryName/path/to/file"
            - Examples:
              - List directory contents: FileOps.ListDirectory("TestRepo", "*")
              - Read a file: FileOps.ReadFile("TestRepo/HelloWorld/Program.cs")
              - List subdirectory: FileOps.ListDirectory("TestRepo/HelloWorld", "*.cs")

            ## Your Capabilities
            You have access to these tools via function calling:

            **FileOps**: Read, write, list, and search files
            **Git**: Check status, create commits, push changes, manage branches
            **Command**: Execute whitelisted shell commands (dotnet build, dotnet test, npm install, npm test)
            **CodeNav**: Navigate code structure, find definitions, search patterns

            ## CRITICAL: How to Respond

            You MUST classify each user request and respond accordingly:

            ### Type 1: Simple Greeting/Capability Question (NO tools needed)
            Examples: "hello", "hi", "what can you do?", "help"
            - Answer directly in 1-2 sentences
            - Do NOT use any tools
            - End response with: <DONE>

            ### Type 2: Information Gathering (READ-ONLY tools - BE THOROUGH)
            Examples: "what files are in src/", "tell me about this project", "what does this repository do?"

            **CRITICAL: Be proactive and investigative!**
            - ALWAYS use tools to gather information
            - For "what does this project do?" or "tell me about repository":
              1. Check the workspace context to see which repositories exist
              2. Try FileOps.ReadFile("RepositoryName/README.md") for each repository
              3. List repository contents: FileOps.ListDirectory("RepositoryName", "*")
              4. Read key files found in the workspace context
              5. Examine actual code to understand purpose
            - **NEVER give up after one failed tool call**
            - **NEVER say "I can't determine" without trying multiple approaches**
            - Provide comprehensive information based on actual code examination
            - End response with: <DONE>

            ### Type 3: Complex Task (MULTIPLE tools, WRITE operations)
            Examples: "add a new feature", "fix the bug in X", "refactor Y"
            - Break into steps
            - Use multiple tools as needed
            - Verify changes with builds/tests
            - End with: <TASK COMPLETE>

            ### Type 4: Blocked/Unclear
            - If you don't understand or need info, end with: <NEED CLARIFICATION>

            ## Response Format Rules

            1. **Always analyze the request type FIRST**
            2. **For simple questions: Answer immediately with <DONE>**
            3. **For info gathering: Use 1-3 tool calls, then <DONE>**
            4. **For complex tasks: Use as many iterations as needed, end with <TASK COMPLETE>**
            5. **NEVER loop without making progress**

            ## Examples

            User: "hello"
            You: "Hello! I'm your coding agent. I can help with code analysis, modifications, git operations, and running builds/tests. What would you like me to do? <DONE>"

            User: "what's in the TestRepo repository?"
            You: [calls FileOps.ListDirectory("TestRepo", "*")]
            You: "The TestRepo repository contains: HelloWorld/ folder. <DONE>"

            User: "what does this repository do?"
            You: [sees TestRepo in workspace context with key files]
            You: [calls FileOps.ReadFile("TestRepo/README.md")] - file not found
            You: [calls FileOps.ListDirectory("TestRepo", "*")] - sees HelloWorld folder
            You: [calls FileOps.ReadFile("TestRepo/HelloWorld/HelloWorld.csproj")] - examines project file
            You: [calls FileOps.ReadFile("TestRepo/HelloWorld/Program.cs")] - reads entry point
            You: "This repository contains a simple .NET console application called HelloWorld that prints 'Hello World' to the console. <DONE>"

            User: "add unit tests for UserService"
            You: [calls multiple tools over several iterations]
            You: "I've added comprehensive unit tests for UserService with 15 test cases covering all public methods. Tests are passing. <TASK COMPLETE>"

            IMPORTANT: If you don't call any tools in your response, you MUST include a completion marker (<DONE>, <TASK COMPLETE>, or <NEED CLARIFICATION>).
            """;
    }

    public async Task ExecuteInstructionAsync(string instruction)
    {
        await _executionLock.WaitAsync();
        try
        {
            _logger.LogInformation("Executing instruction: {Instruction}", instruction);

            _currentState = AgentState.Working;
            _currentActivity = "Processing instruction";

            await _sessionStore.AddConversationMessageAsync("user", instruction);
            await _sessionStore.AddStatusUpdateAsync(_currentState, _currentActivity);

            // Add user message to chat history
            _chatHistory.AddUserMessage(instruction);

            // Execute the agent loop
            await ExecuteAgentLoopAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing instruction");
            _currentState = AgentState.Error;
            _currentActivity = $"Error: {ex.Message}";
            await _sessionStore.AddConversationMessageAsync("system", $"Error: {ex.Message}");
        }
        finally
        {
            _executionLock.Release();
        }
    }

    public async Task SendMessageAsync(string message)
    {
        await _executionLock.WaitAsync();
        try
        {
            _logger.LogInformation("Received message: {Message}", message);

            _currentState = AgentState.Working;
            await _sessionStore.AddConversationMessageAsync("user", message);

            // Add to chat history and continue loop
            _chatHistory.AddUserMessage(message);
            await ExecuteAgentLoopAsync();
        }
        finally
        {
            _executionLock.Release();
        }
    }

    private async Task ExecuteAgentLoopAsync()
    {
        const int maxIterations = 20; // Prevent infinite loops
        var iteration = 0;

        while (iteration < maxIterations && _currentState == AgentState.Working)
        {
            iteration++;
            _logger.LogInformation("Agent loop iteration {Iteration}", iteration);

            try
            {
                // Get response from Azure OpenAI with Polly resilience pipeline for rate limiting
                var results = await _resiliencePipeline.ExecuteAsync(async ct =>
                {
                    return await _chatService.GetChatMessageContentsAsync(
                        _chatHistory,
                        executionSettings: _executionSettings,
                        _kernel,
                        ct);
                }, CancellationToken.None);

                var response = results.FirstOrDefault();
                if (response == null) continue;

                _logger.LogDebug("Agent Response: {Response}", response.Content);

                // Add assistant response to history
                _chatHistory.AddAssistantMessage(response.Content ?? "");

                // Log the response
                var content = response.Content ?? "";
                _logger.LogInformation("Azure OpenAI response: {Response}", content);

                await _sessionStore.AddConversationMessageAsync("assistant", content);
                await _sessionStore.AddThinkingAsync($"Iteration {iteration}: {content}");

                // Check for completion signals (new structured format)
                if (content.Contains("<DONE>", StringComparison.Ordinal) ||
                    content.Contains("<TASK COMPLETE>", StringComparison.Ordinal) ||
                    content.Contains("TASK COMPLETE", StringComparison.OrdinalIgnoreCase))
                {
                    _currentState = AgentState.Complete;
                    _currentActivity = "Task completed";
                    _logger.LogInformation("Agent completed task");
                    break;
                }

                if (content.Contains("<NEED CLARIFICATION>", StringComparison.Ordinal) ||
                    content.Contains("NEED CLARIFICATION", StringComparison.OrdinalIgnoreCase))
                {
                    _currentState = AgentState.NeedsClarification;
                    _currentActivity = "Needs clarification";
                    _logger.LogInformation("Agent needs clarification");
                    break;
                }

                if (content.Contains("<ERROR>", StringComparison.Ordinal))
                {
                    _currentState = AgentState.Error;
                    _currentActivity = "Encountered error";
                    _logger.LogWarning("Agent encountered error");
                    break;
                }

                // If no tools were called and no completion marker, warn and stop
                var toolCallsDetected = response.Metadata?.ContainsKey("ToolCalls") == true;
                if (!toolCallsDetected && iteration > 1)
                {
                    _logger.LogWarning("Agent response iteration {Iteration} had no tool calls and no completion marker. Stopping to prevent infinite loop.", iteration);
                    _currentState = AgentState.Complete;
                    _currentActivity = "Completed (no tools called)";
                    break;
                }

                // Update activity based on recent actions
                _currentActivity = $"Working (iteration {iteration})";
                await _sessionStore.AddStatusUpdateAsync(_currentState, _currentActivity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in agent loop iteration {Iteration}", iteration);
                await _sessionStore.AddThinkingAsync($"Error in iteration {iteration}: {ex.Message}");

                // Continue to next iteration unless it's a critical error
                if (ex is HttpRequestException or TaskCanceledException)
                {
                    // Transient error, continue
                    await Task.Delay(1000);
                    continue;
                }

                throw;
            }
        }

        if (iteration >= maxIterations)
        {
            _logger.LogWarning("Agent reached maximum iterations");
            _currentState = AgentState.NeedsClarification;
            _currentActivity = "Reached iteration limit - needs guidance";
            await _sessionStore.AddConversationMessageAsync("system",
                "Reached maximum iterations. The task may be too complex or unclear. Please provide additional guidance.");
        }

        await _sessionStore.AddStatusUpdateAsync(_currentState, _currentActivity);
    }

    public async Task<AgentStatus> GetStatusAsync()
    {
        var thinking = await _sessionStore.GetRecentThinkingAsync();

        // Get repository statuses
        var repoStatuses = new List<RepositoryStatus>();
        foreach (var repo in _agentSettings.Repositories)
        {
            try
            {
                var status = await _gitService.GetStatusAsync(repo.Name);
                repoStatuses.Add(new RepositoryStatus
                {
                    Name = status.RepositoryName,
                    CurrentBranch = status.CurrentBranch,
                    ModifiedFiles = status.ModifiedFiles.Count,
                    StagedFiles = status.StagedFiles.Count,
                    UntrackedFiles = status.UntrackedFiles.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error getting status for repo: {Repo}", repo.Name);
            }
        }

        return new AgentStatus
        {
            State = _currentState,
            CurrentActivity = _currentActivity,
            ThinkingLog = thinking,
            RepositoryStatuses = repoStatuses.ToArray(),
            LastUpdateTime = DateTimeOffset.UtcNow,
            ExecutionTime = DateTimeOffset.UtcNow - _startTime
        };
    }

    public async Task<HealthCheckResponse> GetHealthAsync()
    {
        await Task.CompletedTask;

        var subsystems = new Dictionary<string, string>
        {
            ["workspace"] = "healthy",
            ["git"] = "healthy",
            ["session_store"] = "healthy",
            ["kernel"] = _kernel != null ? "healthy" : "unhealthy",
            ["azure_openai"] = _chatService != null ? "healthy" : "unhealthy"
        };

        return new HealthCheckResponse(
            Status: subsystems.Values.All(v => v == "healthy") ? "healthy" : "degraded",
            Subsystems: subsystems,
            Uptime: DateTimeOffset.UtcNow - _startTime,
            LastActivity: DateTimeOffset.UtcNow);
    }

    public async Task StopAsync()
    {
        _logger.LogInformation("Stopping agent");
        _currentState = AgentState.Idle;
        _currentActivity = "Stopped";
        await _sessionStore.AddStatusUpdateAsync(_currentState, _currentActivity);
    }
}
