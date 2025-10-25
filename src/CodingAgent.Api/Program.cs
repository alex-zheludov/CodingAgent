using CodingAgent.Configuration;
using CodingAgent.Configuration.Validators;
using CodingAgent.Data;
using CodingAgent.Endpoints;
using CodingAgent.Extensions;
using CodingAgent.Plugins;
using CodingAgent.Processes.Steps;
using CodingAgent.Services;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/codingagent-api-.txt",
        rollingInterval: RollingInterval.Day,
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);

// Use Serilog for logging
builder.Services.AddSerilog();

// Add user secrets support
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

// Add Multi-Agent Orchestration Services
builder.Services.AddSingleton<IKernelFactory, KernelFactory>();
builder.Services.AddScoped<IOrchestrationService, OrchestrationService>();

// Register Process Steps
builder.Services.AddTransient<IntentClassifierStep>();
builder.Services.AddTransient<ContextAgentStep>();
builder.Services.AddTransient<ResearchAgentStep>();
builder.Services.AddTransient<PlanningAgentStep>();
builder.Services.AddTransient<ExecutionAgentStep>();
builder.Services.AddTransient<SummaryAgentStep>();

// Add OpenAPI/Swagger
builder.Services.AddOpenApi();

// Add health checks
builder.Services.AddHealthChecks();

var app = builder.Build();

// Run initialization at startup
var initService = app.Services.GetRequiredService<IInitializationService>();
workspaceContext = await initService.InitializeAsync();

var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("CodingAgent API initialized and ready");

// Configure middleware
if (app.Environment.IsDevelopment())
{
    // Map OpenAPI document at /openapi/v1.json
    app.MapOpenApi();

    // Map Scalar UI at /scalar/v1
    app.MapScalarApiReference(options =>
    {
        options
            .WithTitle("CodingAgent API")
            .WithTheme(ScalarTheme.Purple)
            .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
    });
}

app.UseHttpsRedirection();

// Map endpoints
var endpointDefinitions = new IEndpointDefinition[]
{
    new AgentEndpoints(),
    new OrchestrationEndpoints()
};

foreach (var definition in endpointDefinitions)
{
    definition.DefineEndpoints(app);
}

// Map health checks
app.MapHealthChecks("/health");

app.Run();
