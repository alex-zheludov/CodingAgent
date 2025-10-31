using MicroMind.Core.Models;

namespace MicroMind.Core.Abstractions;

public interface IInferenceRuntime
{
    Task LoadModelAsync(string modelPath, CancellationToken cancellationToken = default);

    Task<CompletionResponse> GenerateCompletionAsync(CompletionRequest request, CancellationToken cancellationToken = default);

    IAsyncEnumerable<CompletionChunk> GenerateStreamingCompletionAsync(CompletionRequest request, CancellationToken cancellationToken = default);

    void UnloadModel();

    bool IsModelLoaded { get; }
}
