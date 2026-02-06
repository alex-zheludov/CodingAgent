using CodeVectorization.Core.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CodeVectorization.Core.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCodeVectorization(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AzureOpenAISettings>()
            .Bind(configuration.GetSection(AzureOpenAISettings.SectionName));

        services.AddOptions<QdrantSettings>()
            .Bind(configuration.GetSection(QdrantSettings.SectionName));

        services.AddOptions<IndexingSettings>()
            .Bind(configuration.GetSection(IndexingSettings.SectionName));

        return services;
    }
}
