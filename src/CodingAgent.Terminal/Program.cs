using CodingAgent.Configuration;
using CodingAgent.Configuration.Validators;
using CodingAgent.Data;
using CodingAgent.Extensions;
using CodingAgent.Plugins;
using CodingAgent.Processes.Steps;
using CodingAgent.Services;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;

// Configure Serilog before creating the host builder
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/codingagent-.txt",
        rollingInterval: RollingInterval.Day,
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

var builder = Host.CreateApplicationBuilder(args);

// Use Serilog for logging
builder.Services.AddSerilog();

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

// Add Model Settings for Multi-Agent Orchestration
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var modelSettings = new ModelSettings();
    config.GetSection(ModelSettings.SectionName).Bind(modelSettings);
    return modelSettings;
});

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

// Add agent (legacy - keeping for backward compatibility if needed)
// builder.Services.AddScoped<ICodingAgent, CodingCodingAgent>();

// Add Multi-Agent Orchestration Services
builder.Services.AddSingleton<IKernelFactory, KernelFactory>();
builder.Services.AddScoped<IOrchestrationService, OrchestrationService>();

// Register Process Steps
builder.Services.AddTransient<IntentClassifierStep>();
builder.Services.AddTransient<ResearchAgentStep>();
builder.Services.AddTransient<PlanningAgentStep>();
builder.Services.AddTransient<ExecutionAgentStep>();
builder.Services.AddTransient<SummaryAgentStep>();

var app = builder.Build();

// Run initialization at startup
var initService = app.Services.GetRequiredService<IInitializationService>();
workspaceContext = await initService.InitializeAsync();

// Create a scope for the orchestration service
using var scope = app.Services.CreateScope();
var orchestrationService = scope.ServiceProvider.GetRequiredService<IOrchestrationService>();
var agentSettings = scope.ServiceProvider.GetRequiredService<IOptions<AgentSettings>>().Value;
var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

Console.WriteLine("===========================================");
Console.WriteLine("   Coding Agent Terminal (Kernel Process)");
Console.WriteLine("===========================================");
Console.WriteLine($"Session ID: {agentSettings.Session.SessionId}");
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
        var status = await orchestrationService.GetStatusAsync();
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"Status: {status}");
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

        // Execute the instruction using the orchestration service
        var result = await orchestrationService.ProcessInstructionAsync(input);

        // Display the summary result
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Agent:");
        Console.ResetColor();
        Console.WriteLine(result.Summary);

        // Show key findings if available (for research questions)
        if (result.KeyFindings != null && result.KeyFindings.Any())
        {
            Console.WriteLine();
            foreach (var finding in result.KeyFindings)
            {
                Console.WriteLine(finding);
            }
        }

        // Show metrics if available
        if (result.Metrics != null)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"Steps: {result.Metrics.StepsCompleted}/{result.Metrics.StepsTotal} | Success Rate: {result.Metrics.SuccessRate}");
            Console.ResetColor();
        }

        // Show status
        var status = await orchestrationService.GetStatusAsync();
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"[{status}]");
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
