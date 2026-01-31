using CodingAgent.Core.Configuration;
using CodingAgent.Core.Configuration.Validators;
using CodingAgent.Core.Data;
using CodingAgent.Core.Plugins;
using CodingAgent.Core.Services;
using CodingAgent.Core.Workflow;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace CodingAgent.Core.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all CodingAgent services, configuration, plugins, and workflows.
    /// </summary>
    public static IServiceCollection AddCodingAgent(this IServiceCollection services, IConfiguration configuration)
    {
        AddConfiguration(services);
        AddCoreServices(services);
        AddPlugins(services);
        AddWorkflows(services);

        return services;
    }

    private static void AddConfiguration(IServiceCollection services)
    {
        services.AddOptions<AzureOpenAISettings>()
            .BindConfiguration(AzureOpenAISettings.SectionName)
            .ValidateUsingFluentValidator();

        services.AddOptions<AgentSettings>()
            .BindConfiguration(AgentSettings.SectionName)
            .ValidateUsingFluentValidator();

        services.AddOptions<GitSettings>()
            .BindConfiguration(GitSettings.SectionName)
            .ValidateUsingFluentValidator();

        services.AddOptions<OrchestratorSettings>()
            .BindConfiguration(OrchestratorSettings.SectionName);

        services.AddOptions<SecuritySettings>()
            .BindConfiguration(SecuritySettings.SectionName);

        services.AddSingleton(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var modelSettings = new ModelSettings();
            config.GetSection(ModelSettings.SectionName).Bind(modelSettings);
            return modelSettings;
        });

        services.AddValidatorsFromAssemblyContaining<AzureOpenAISettingsValidator>();
    }

    private static void AddCoreServices(IServiceCollection services)
    {
        services.AddDbContext<SessionDbContext>((serviceProvider, options) =>
        {
            var workspaceManager = serviceProvider.GetRequiredService<IWorkspaceManager>();
            var dbPath = Path.Combine(workspaceManager.GetSessionDataPath(), "session.db");
            options.UseSqlite($"Data Source={dbPath}");
        });

        services.AddSingleton<IWorkspaceManager, WorkspaceManager>();
        services.AddSingleton<IGitService, GitService>();
        services.AddSingleton<ISecurityService, SecurityService>();
        services.AddScoped<IInitializationService, InitializationService>();
        services.AddScoped<ISessionStore, SessionStore>();
    }

    private static void AddPlugins(IServiceCollection services)
    {
        services.AddScoped<FileOperationsPlugin>();
        services.AddScoped<GitPlugin>();
        services.AddScoped<CommandPlugin>();
        services.AddScoped<CodeNavigationPlugin>();
    }

    private static void AddWorkflows(IServiceCollection services)
    {
        services.AddScoped<CodingAgentWorkflowBuilder>();
        services.AddScoped<IWorkflowService, WorkflowService>();
    }
}
