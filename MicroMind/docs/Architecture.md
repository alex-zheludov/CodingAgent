# MicroMind Architecture

This document describes the architecture and design principles of the MicroMind library.

## Overview

MicroMind is designed following SOLID principles and clean architecture patterns. The library is organized into distinct layers with clear separation of concerns, making it maintainable, testable, and extensible.

## Architectural Layers

### 1. Core Layer (MicroMind.Core)

The Core layer contains abstractions, models, and configuration that define the contracts for the entire system. It has minimal dependencies and serves as the foundation for all other layers.

**Key Components:**

- **Abstractions**: Interface definitions for core services
  - `IModelManager`: Model lifecycle management
  - `IInferenceRuntime`: Inference execution abstraction
  - `IDownloadProvider`: File download abstraction

- **Models**: Domain models and data structures
  - `CompletionRequest`, `CompletionResponse`, `CompletionChunk`
  - `ConversationMessage`, `MessageRole`
  - `ModelMetadata`, `ModelCapabilities`
  - `DownloadProgress`
  - `FinishReason` enum

- **Configuration**: Strongly-typed configuration classes
  - `MicroMindOptions`: Root configuration
  - `ModelConfiguration`: Model source and version
  - `InferenceConfiguration`: Inference parameters
  - `DownloadConfiguration`: Download retry and timeout settings
  - `CacheConfiguration`: Cache location and validation

- **Exceptions**: Custom exception hierarchy
  - `MicroMindException`: Base exception
  - `ModelDownloadException`: Download failures
  - `ModelValidationException`: Validation failures
  - `ModelLoadException`: Model loading failures
  - `UnsupportedModelException`: Unsupported model formats

**Design Principles:**
- No external dependencies except Microsoft.Extensions abstractions
- Interface Segregation: Small, focused interfaces
- Dependency Inversion: Depend on abstractions, not implementations

### 2. Infrastructure Layer (MicroMind.Infrastructure)

The Infrastructure layer implements core abstractions for cross-cutting concerns like file downloads, HTTP communication, and file system operations.

**Key Components:**

- **ModelManager**: Implements `IModelManager`
  - Thread-safe model availability checking
  - Automatic model download with progress reporting
  - Checksum validation
  - Cache management and invalidation
  - Metadata retrieval

- **HttpDownloadProvider**: Implements `IDownloadProvider`
  - HTTP file downloads with retry logic
  - Exponential backoff for transient failures
  - Progress reporting during download
  - Checksum validation (SHA256, SHA512, MD5)
  - Temporary file handling with cleanup

**Design Principles:**
- Single Responsibility: Each class has one reason to change
- Open/Closed: Open for extension, closed for modification
- Thread-safety using `SemaphoreSlim` for concurrent operations

### 3. Runtime Layer (MicroMind.Runtime.LLamaSharp)

The Runtime layer provides concrete implementations of inference engines. Currently supports LLamaSharp for GGUF model inference.

**Key Components:**

- **LLamaSharpInferenceRuntime**: Implements `IInferenceRuntime`
  - Thread-safe model loading and inference
  - Lazy model loading on first inference request
  - Streaming and non-streaming completion support
  - Prompt building with system prompt, history, and user prompt
  - Inference parameter configuration using sampling pipelines
  - Resource management and cleanup

**Design Principles:**
- Liskov Substitution: Can be replaced with other runtime implementations
- Resource management: Proper disposal of native resources
- Async/await patterns throughout

### 4. Integration Layer (MicroMind.Integration.AgentFramework)

The Integration layer provides framework-specific bindings and extension methods for seamless integration with Microsoft Agent Framework.

**Key Components:**

- **MicroMindChatClient**: Implements `IChatClient`
  - Lazy initialization of model on first use
  - Conversion between `ChatMessage` and `CompletionRequest` formats
  - Support for streaming and non-streaming completions
  - Metadata exposure through `ChatClientMetadata`
  - Usage tracking (input/output token counts)

- **ServiceCollectionExtensions**: Extension methods for dependency injection
  - Three registration overloads (IConfiguration, Action, default)
  - Registers all required services with appropriate lifetimes
  - Configures HttpClient for downloads
  - Options validation

**Design Principles:**
- Dependency Injection: Full support for .NET DI patterns
- Configuration flexibility: Multiple configuration sources
- Framework compatibility: Implements standard interfaces

## Design Patterns

### 1. Dependency Injection

All components use constructor injection for dependencies:

```csharp
public class ModelManager : IModelManager
{
    private readonly IDownloadProvider _downloadProvider;
    private readonly IOptions<MicroMindOptions> _options;
    private readonly ILogger<ModelManager> _logger;

    public ModelManager(
        IDownloadProvider downloadProvider,
        IOptions<MicroMindOptions> options,
        ILogger<ModelManager> logger)
    {
        _downloadProvider = downloadProvider ?? throw new ArgumentNullException(nameof(downloadProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
}
```

### 2. Options Pattern

Configuration uses the .NET Options pattern:

```csharp
public class MicroMindOptions
{
    public ModelConfiguration Model { get; set; } = new();
    public InferenceConfiguration Inference { get; set; } = new();
    public DownloadConfiguration Download { get; set; } = new();
    public CacheConfiguration Cache { get; set; } = new();
}
```

### 3. Repository Pattern

`IModelManager` acts as a repository for model files:

```csharp
public interface IModelManager
{
    Task EnsureModelAvailableAsync(CancellationToken cancellationToken = default);
    Task<string> GetModelPathAsync(CancellationToken cancellationToken = default);
    Task InvalidateCacheAsync(CancellationToken cancellationToken = default);
    ModelMetadata GetMetadata();
}
```

### 4. Strategy Pattern

`IInferenceRuntime` allows different inference strategies:

```csharp
public interface IInferenceRuntime
{
    Task LoadModelAsync(string modelPath, CancellationToken cancellationToken = default);
    Task<CompletionResponse> GenerateCompletionAsync(CompletionRequest request, CancellationToken cancellationToken = default);
    IAsyncEnumerable<CompletionChunk> GenerateStreamingCompletionAsync(CompletionRequest request, CancellationToken cancellationToken = default);
    void UnloadModel();
    bool IsModelLoaded { get; }
}
```

### 5. Adapter Pattern

`MicroMindChatClient` adapts internal interfaces to `IChatClient`:

```csharp
public class MicroMindChatClient : IChatClient
{
    private readonly IModelManager _modelManager;
    private readonly IInferenceRuntime _inferenceRuntime;
    
    public async Task<ChatCompletion> CompleteAsync(
        IList<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        var request = ConvertToCompletionRequest(chatMessages, options);
        var response = await _inferenceRuntime.GenerateCompletionAsync(request, cancellationToken);
        return ConvertToChatCompletion(response);
    }
}
```

## Thread Safety

### Concurrent Operations

All public APIs are thread-safe and support concurrent operations:

- **ModelManager**: Uses `SemaphoreSlim` to ensure only one download occurs at a time
- **LLamaSharpInferenceRuntime**: Uses `SemaphoreSlim` to serialize inference requests
- **HttpDownloadProvider**: Each download operation is independent

### Async/Await

All I/O operations use async/await patterns:

```csharp
public async Task EnsureModelAvailableAsync(CancellationToken cancellationToken = default)
{
    await _downloadLock.WaitAsync(cancellationToken);
    try
    {
        if (File.Exists(modelPath))
        {
            return;
        }
        
        await _downloadProvider.DownloadAsync(sourceUrl, modelPath, progress, cancellationToken);
    }
    finally
    {
        _downloadLock.Release();
    }
}
```

## Error Handling

### Exception Hierarchy

Custom exceptions provide specific error information:

```
MicroMindException (base)
├── ModelDownloadException (download failures)
├── ModelValidationException (checksum/validation failures)
├── ModelLoadException (model loading failures)
└── UnsupportedModelException (unsupported formats)
```

### Retry Logic

Transient failures are handled with exponential backoff:

```csharp
private async Task<Stream> DownloadCoreAsync(string sourceUrl, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
{
    var attempt = 0;
    var delay = _options.Value.Download.RetryDelayMs;
    
    while (attempt < _options.Value.Download.MaxRetries)
    {
        try
        {
            return await DownloadWithProgressAsync(sourceUrl, progress, cancellationToken);
        }
        catch (HttpRequestException ex) when (attempt < _options.Value.Download.MaxRetries - 1)
        {
            await Task.Delay(delay, cancellationToken);
            delay *= 2; // Exponential backoff
            attempt++;
        }
    }
}
```

## Testing Strategy

### Unit Tests

Each layer has comprehensive unit tests:

- **Core.Tests**: Configuration, models, and domain logic
- **Infrastructure.Tests**: ModelManager, HttpDownloadProvider
- **Runtime.Tests**: LLamaSharpInferenceRuntime
- **Integration.Tests**: MicroMindChatClient, ServiceCollectionExtensions

### Test Organization

Tests follow the Arrange-Act-Assert (AAA) pattern:

```csharp
[Fact]
public async Task EnsureModelAvailableAsync_WhenModelExists_ShouldNotDownload()
{
    // Arrange
    var modelPath = Path.Combine(_tempDir, "model.gguf");
    File.WriteAllText(modelPath, "test");
    
    // Act
    await _modelManager.EnsureModelAvailableAsync();
    
    // Assert
    _mockDownloadProvider.Verify(x => x.DownloadAsync(
        It.IsAny<string>(),
        It.IsAny<string>(),
        It.IsAny<IProgress<DownloadProgress>>(),
        It.IsAny<CancellationToken>()), Times.Never);
}
```

### Mocking

Tests use Moq for dependency mocking:

```csharp
var mockDownloadProvider = new Mock<IDownloadProvider>();
mockDownloadProvider
    .Setup(x => x.DownloadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IProgress<DownloadProgress>>(), It.IsAny<CancellationToken>()))
    .Returns(Task.CompletedTask);
```

## Extensibility

### Adding New Runtimes

To add a new inference runtime:

1. Create a new project (e.g., `MicroMind.Runtime.OnnxRuntime`)
2. Implement `IInferenceRuntime` interface
3. Register in dependency injection:

```csharp
services.AddSingleton<IInferenceRuntime, OnnxInferenceRuntime>();
```

### Adding New Integrations

To add integration with another framework:

1. Create a new project (e.g., `MicroMind.Integration.SemanticKernel`)
2. Implement framework-specific interfaces
3. Provide extension methods for registration

### Custom Download Providers

To implement custom download logic:

1. Implement `IDownloadProvider` interface
2. Register in dependency injection:

```csharp
services.AddSingleton<IDownloadProvider, CustomDownloadProvider>();
```

## Performance Considerations

### Memory Management

- Models are loaded lazily on first use
- Models stay in memory until explicitly unloaded
- Streaming completions reduce memory pressure for long outputs

### Caching

- Models are cached after first download
- Cache validation prevents re-downloading unchanged models
- Cache location is configurable for different storage strategies

### Concurrency

- Inference requests are serialized to prevent resource contention
- Download operations use a single lock to prevent duplicate downloads
- Async operations throughout prevent thread blocking

## Security

### Download Security

- HTTPS-only downloads
- SSL certificate validation
- Checksum verification
- Temporary file handling with cleanup

### Privacy

- No telemetry by default
- Configurable logging levels
- Local inference (no data sent to external services)

## Future Enhancements

Potential areas for extension:

- Multiple model support in single application
- Model switching at runtime
- GPU acceleration configuration UI
- Model quantization utilities
- Performance benchmarking tools
- Semantic Kernel integration
- Additional inference runtime support (ONNX Runtime)

## Conclusion

MicroMind's architecture prioritizes maintainability, testability, and extensibility through:

- Clear separation of concerns across layers
- SOLID principles throughout
- Comprehensive abstraction boundaries
- Thread-safe concurrent operations
- Extensive test coverage
- Flexible configuration options

This design enables easy extension and modification while maintaining stability and reliability.
