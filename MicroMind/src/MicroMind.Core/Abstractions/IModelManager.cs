using MicroMind.Core.Models;

namespace MicroMind.Core.Abstractions;

public interface IModelManager
{
    Task EnsureModelAvailableAsync(CancellationToken cancellationToken = default);

    Task<string> GetModelPathAsync(CancellationToken cancellationToken = default);

    Task InvalidateCacheAsync(CancellationToken cancellationToken = default);

    ModelMetadata GetMetadata();
}
