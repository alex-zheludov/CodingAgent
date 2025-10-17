using CodingAgent.Configuration;
using CodingAgent.Configuration.Validators;
using CodingAgent.Data;
using CodingAgent.Endpoints;
using CodingAgent.Plugins;
using CodingAgent.Services;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel.ChatCompletion;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add configuration with validation
builder.Services.AddOptions<AzureOpenAISettings>()
    .BindConfiguration(AzureOpenAISettings.SectionName)
    .ValidateOnStart();

builder.Services.AddOptions<AgentSettings>()
    .BindConfiguration(AgentSettings.SectionName)
    .ValidateOnStart();

builder.Services.AddOptions<GitSettings>()
    .BindConfiguration(GitSettings.SectionName)
    .ValidateOnStart();

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

// Add plugins (required by AgentOrchestrator)
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

// Add orchestrator (depends on plugins and workspace context)
builder.Services.AddScoped<ICodingAgent, CodingAgent.Services.CodingCodingAgent>();

// Add OpenAPI/Swagger
builder.Services.AddOpenApi();

// Add health checks
builder.Services.AddHealthChecks();

var app = builder.Build();

// Run initialization at startup
// Note: InitializationService is a singleton, but it internally creates a scope
// to resolve scoped dependencies like ISessionStore for database initialization
var initService = app.Services.GetRequiredService<IInitializationService>();
workspaceContext = await initService.InitializeAsync();

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
    new AgentEndpoints()
};

foreach (var definition in endpointDefinitions)
{
    definition.DefineEndpoints(app);
}

// Map health checks
app.MapHealthChecks("/health");

app.Run();
