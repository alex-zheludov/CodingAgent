using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MicroMind.Core.Abstractions;
using MicroMind.Core.Configuration;
using MicroMind.Infrastructure;
using MicroMind.Runtime.LLamaSharp;

namespace MicroMind.Integration.AgentFramework;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMicroMind(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<MicroMindOptions>(configuration);
        services.AddOptions<MicroMindOptions>()
            .Bind(configuration)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton<IDownloadProvider, HttpDownloadProvider>();
        services.TryAddSingleton<IModelManager, ModelManager>();
        services.TryAddSingleton<IInferenceRuntime, LLamaSharpInferenceRuntime>();

        services.AddHttpClient<IDownloadProvider, HttpDownloadProvider>();

        services.TryAddSingleton<IChatClient, MicroMindChatClient>();

        return services;
    }

    public static IServiceCollection AddMicroMind(
        this IServiceCollection services,
        Action<MicroMindOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureOptions);

        services.Configure(configureOptions);
        services.AddOptions<MicroMindOptions>()
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton<IDownloadProvider, HttpDownloadProvider>();
        services.TryAddSingleton<IModelManager, ModelManager>();
        services.TryAddSingleton<IInferenceRuntime, LLamaSharpInferenceRuntime>();

        services.AddHttpClient<IDownloadProvider, HttpDownloadProvider>();

        services.TryAddSingleton<IChatClient, MicroMindChatClient>();

        return services;
    }

    public static IServiceCollection AddMicroMind(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.Configure<MicroMindOptions>(options =>
        {
        });

        services.AddOptions<MicroMindOptions>()
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton<IDownloadProvider, HttpDownloadProvider>();
        services.TryAddSingleton<IModelManager, ModelManager>();
        services.TryAddSingleton<IInferenceRuntime, LLamaSharpInferenceRuntime>();

        services.AddHttpClient<IDownloadProvider, HttpDownloadProvider>();

        services.TryAddSingleton<IChatClient, MicroMindChatClient>();

        return services;
    }
}
