using CodingAgent.Agents;
using CodingAgent.Configuration;
using CodingAgent.Configuration.Validators;
using CodingAgent.Data;
using CodingAgent.Extensions;
using CodingAgent.Plugins;
using CodingAgent.Services;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddUserSecrets<Program>();

// Add configuration with validation
builder.Services.AddOptions<AzureOpenAISettings>()
    .BindConfiguration(AzureOpenAISettings.SectionName)
    .ValidateUsingFluentValidator();

builder.Services.AddOptions<AgentSettings>()
    .BindConfiguration(AgentSettings.SectionName)
    .ValidateUsingFluentValidator();

builder.Services.AddOptions<GitSettings>()
    .BindConfiguration(GitSettings.SectionName)
    .ValidateUsingFluentValidator();

builder.Services.AddOptions<OrchestratorSettings>()
    .BindConfiguration(OrchestratorSettings.SectionName);

builder.Services.AddOptions<SecuritySettings>()
    .BindConfiguration(SecuritySettings.SectionName);

// Add FluentValidation validators
builder.Services.AddValidatorsFromAssemblyContaining<AzureOpenAISettingsValidator>();

// Add database
builder.Services.AddDbContext<SessionDbContext>((serviceProvider, options) =>
{
    var agentSettings = serviceProvider.GetRequiredService<IOptions<AgentSettings>>().Value;
    var workspaceManager = serviceProvider.GetRequiredService<IWorkspaceManager>();
    var dbPath = Path.Combine(workspaceManager.GetSessionDataPath(), "session.db");
    options.UseSqlite($"Data Source={dbPath}");
});

// Add services
builder.Services.AddSingleton<IWorkspaceManager, WorkspaceManager>();
builder.Services.AddSingleton<IGitService, GitService>();
builder.Services.AddSingleton<ISecurityService, SecurityService>();
builder.Services.AddSingleton<IInitializationService, InitializationService>();
builder.Services.AddScoped<ISessionStore, SessionStore>();

// Add plugins (required by CodingAgent)
builder.Services.AddScoped<FileOpsPlugin>();
builder.Services.AddScoped<GitPlugin>();
builder.Services.AddScoped<CommandPlugin>();
builder.Services.AddScoped<CodeNavPlugin>();

// Initialize the agent and build workspace context
WorkspaceContext workspaceContext = null!;
builder.Services.AddSingleton(sp =>
{
    // This will be populated after initialization
    return workspaceContext;
});

// Add agent
builder.Services.AddScoped<ICodingAgent, CodingCodingAgent>();

var app = builder.Build();

// Run initialization at startup
var initService = app.Services.GetRequiredService<IInitializationService>();
workspaceContext = await initService.InitializeAsync();

// Create a scope for the agent
using var scope = app.Services.CreateScope();
var agent = scope.ServiceProvider.GetRequiredService<ICodingAgent>();
var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

Console.WriteLine("===========================================");
Console.WriteLine("   Coding Agent Terminal");
Console.WriteLine("===========================================");
Console.WriteLine($"Session ID: {agent.SessionId}");
Console.WriteLine();
Console.WriteLine("Type your instructions and press Enter.");
Console.WriteLine("Type 'exit' to quit, 'status' for status.");
Console.WriteLine("===========================================");
Console.WriteLine();

while (true)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.Write("You: ");
    Console.ResetColor();

    var input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input))
        continue;

    if (input.Equals("exit", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("Goodbye!");
        break;
    }

    if (input.Equals("status", StringComparison.OrdinalIgnoreCase))
    {
        var status = await agent.GetStatusAsync();
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"Status: {status.State}");
        Console.WriteLine($"Activity: {status.CurrentActivity}");
        Console.WriteLine($"Execution Time: {status.ExecutionTime}");
        Console.ResetColor();
        Console.WriteLine();
        continue;
    }

    try
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Agent: Processing your request...");
        Console.ResetColor();

        // Execute the instruction
        await agent.ExecuteInstructionAsync(input);

        // Get the conversation history to show the response
        var sessionStore = scope.ServiceProvider.GetRequiredService<ISessionStore>();
        var messages = await sessionStore.GetConversationHistoryAsync(10, 1);

        // Find the last assistant message
        var lastAssistantMessage = messages
            .Where(m => m.Role == "assistant")
            .OrderByDescending(m => m.Timestamp)
            .FirstOrDefault();

        if (lastAssistantMessage != null)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Agent:");
            Console.ResetColor();
            Console.WriteLine(lastAssistantMessage.Content);
        }

        // Show status
        var status = await agent.GetStatusAsync();
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"[{status.State}] {status.CurrentActivity}");
        Console.ResetColor();
    }
    catch (Exception ex)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Error: {ex.Message}");
        Console.ResetColor();
        logger.LogError(ex, "Error processing instruction");
    }

    Console.WriteLine();
}
