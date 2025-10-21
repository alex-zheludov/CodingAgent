using CodingAgent.Configuration;
using CodingAgent.Models.Orchestration;
using CodingAgent.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace CodingAgent.Services;

public interface IKernelFactory
{
    Kernel CreateKernel(AgentCapability capability);
}

public class KernelFactory : IKernelFactory
{
    private readonly ModelSettings _modelSettings;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IServiceProvider _serviceProvider;

    public KernelFactory(
        ModelSettings modelSettings,
        ILoggerFactory loggerFactory,
        IServiceProvider serviceProvider)
    {
        _modelSettings = modelSettings;
        _loggerFactory = loggerFactory;
        _serviceProvider = serviceProvider;
    }

    public Kernel CreateKernel(AgentCapability capability)
    {
        var config = capability switch
        {
            AgentCapability.IntentClassification => _modelSettings.IntentClassifier,
            AgentCapability.Research => _modelSettings.Research,
            AgentCapability.Planning => _modelSettings.Planning,
            AgentCapability.Summary => _modelSettings.Summary,
            AgentCapability.Execution => _modelSettings.Execution,
            _ => throw new ArgumentException($"Unknown capability: {capability}")
        };

        var builder = Kernel.CreateBuilder();

        // Add Azure OpenAI chat completion (all agents use Azure OpenAI)
        builder.AddAzureOpenAIChatCompletion(
            deploymentName: config.Model,
            endpoint: _modelSettings.Endpoint,
            apiKey: _modelSettings.ApiKey);

        // Add plugins based on capability
        RegisterPlugins(builder, capability);

        // Add logging
        builder.Services.AddSingleton(_loggerFactory);

        return builder.Build();
    }

    private void RegisterPlugins(IKernelBuilder builder, AgentCapability capability)
    {
        // Intent Classifier: No plugins needed
        // Summary Agent: No plugins needed (works with state data only)
        if (capability == AgentCapability.IntentClassification ||
            capability == AgentCapability.Summary)
            return;

        // Research Agent: Read-only plugins
        if (capability == AgentCapability.Research)
        {
            builder.Plugins.AddFromObject(_serviceProvider.GetRequiredService<FileOpsPlugin>(), "FileOps");
            builder.Plugins.AddFromObject(_serviceProvider.GetRequiredService<CodeNavPlugin>(), "CodeNav");
            builder.Plugins.AddFromObject(_serviceProvider.GetRequiredService<GitPlugin>(), "Git");
            return;
        }

        // Planning Agent: All plugins for analysis
        // Execution Agent: All plugins for operations
        if (capability == AgentCapability.Planning || capability == AgentCapability.Execution)
        {
            builder.Plugins.AddFromObject(_serviceProvider.GetRequiredService<FileOpsPlugin>(), "FileOps");
            builder.Plugins.AddFromObject(_serviceProvider.GetRequiredService<GitPlugin>(), "Git");
            builder.Plugins.AddFromObject(_serviceProvider.GetRequiredService<CommandPlugin>(), "Command");
            builder.Plugins.AddFromObject(_serviceProvider.GetRequiredService<CodeNavPlugin>(), "CodeNav");
        }
    }
}
