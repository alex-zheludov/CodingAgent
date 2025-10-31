# MicroMind

A .NET class library that wraps a tiny LLM (Large Language Model) and exposes it through standardized interfaces compatible with Microsoft Agent Framework and Semantic Kernel. The library handles model lifecycle management (download, caching, initialization) transparently and provides seamless integration into agent-based applications.

## Features

- **Zero-Configuration Model Hosting**: Automatic model download and caching on first use
- **Transparent Model Management**: Handles download, validation, and initialization automatically
- **Standard Interfaces**: Compatible with Microsoft.Extensions.AI (IChatClient) and Semantic Kernel
- **Local Inference**: Run LLMs locally without external API dependencies
- **Streaming Support**: Both streaming and non-streaming completion modes
- **Thread-Safe**: Concurrent inference requests handled safely
- **Dependency Injection**: Full support for .NET dependency injection patterns
- **SOLID Architecture**: Maintainable, testable, and extensible design

## Quick Start

### Installation

Add the MicroMind.Integration.AgentFramework package to your project:

```bash
dotnet add package MicroMind.Integration.AgentFramework
```

### Basic Usage

```csharp
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MicroMind.Integration.AgentFramework;

var builder = Host.CreateApplicationBuilder(args);

// Register MicroMind services
builder.Services.AddMicroMind();

var host = builder.Build();

// Get the chat client
var chatClient = host.Services.GetRequiredService<IChatClient>();

// Send a message
var response = await chatClient.CompleteAsync(
    [
        new ChatMessage(ChatRole.System, "You are a helpful assistant."),
        new ChatMessage(ChatRole.User, "What is the capital of France?")
    ]);

Console.WriteLine(response.Message.Text);
```

### Streaming Completions

```csharp
await foreach (var chunk in chatClient.CompleteStreamingAsync(
    [
        new ChatMessage(ChatRole.User, "Tell me a short story.")
    ]))
{
    Console.Write(chunk.Text);
}
```

### Configuration

Configure MicroMind through appsettings.json:

```json
{
  "MicroMind": {
    "Model": {
      "SourceUrl": "https://huggingface.co/microsoft/Phi-3-mini-4k-instruct-gguf/resolve/main/Phi-3-mini-4k-instruct-q4.gguf",
      "Checksum": "",
      "Version": "1.0"
    },
    "Inference": {
      "Temperature": 0.7,
      "MaxTokens": 2048,
      "TopP": 0.95,
      "ContextSize": 4096,
      "GpuLayers": 0
    },
    "Cache": {
      "CachePath": null
    }
  }
}
```

Or configure programmatically:

```csharp
builder.Services.AddMicroMind(options =>
{
    options.Inference.Temperature = 0.8f;
    options.Inference.MaxTokens = 4096;
    options.Cache.CachePath = "/custom/cache/path";
});
```

## Architecture

MicroMind follows a layered architecture with clear separation of concerns:

- **Core Layer**: Abstractions, models, and configuration
- **Infrastructure Layer**: Model download, file system operations, HTTP client management
- **Runtime Layer**: Inference engine implementations (LLamaSharp)
- **Integration Layer**: Framework-specific bindings (Microsoft Agent Framework, Semantic Kernel)

### Key Components

- **IModelManager**: Manages model availability, download, and caching
- **IInferenceRuntime**: Abstracts inference engine specifics, executes completions
- **IDownloadProvider**: Handles file downloads with retry logic and progress reporting
- **MicroMindChatClient**: Implements IChatClient for Microsoft Agent Framework integration

## Default Model

MicroMind uses Microsoft Phi-3-mini-4k-instruct (GGUF Q4 quantized) as the default model:

- Size: ~2.3GB
- Context: 4096 tokens
- License: MIT (commercial use allowed)
- Quality: Good balance of performance and size

The model is automatically downloaded on first use and cached in the user's application data directory.

## Requirements

- .NET 9.0 or later
- Windows (x64, ARM64), Linux (x64, ARM64), or macOS (x64, ARM64)
- ~2.5GB disk space for model cache
- 4GB+ RAM recommended for inference

## Project Structure

```
MicroMind/
├── src/
│   ├── MicroMind.Core/                    # Core abstractions and models
│   ├── MicroMind.Infrastructure/          # Download, file system, HTTP
│   ├── MicroMind.Runtime.LLamaSharp/      # LLamaSharp implementation
│   └── MicroMind.Integration.AgentFramework/  # Microsoft Agent Framework bindings
├── tests/
│   ├── MicroMind.Core.Tests/
│   ├── MicroMind.Infrastructure.Tests/
│   ├── MicroMind.Runtime.Tests/
│   └── MicroMind.Integration.Tests/
├── samples/
│   └── ConsoleApp.AgentFramework/
└── docs/
    ├── QuickStart.md
    └── Architecture.md
```

## Documentation

- [Quick Start Guide](docs/QuickStart.md) - Detailed getting started guide
- [Architecture](docs/Architecture.md) - Detailed architecture documentation

## Testing

The project includes comprehensive unit tests with 80%+ code coverage:

```bash
dotnet test
```

## License

This project is part of the CodingAgent repository.

## Contributing

Contributions are welcome! Please ensure all tests pass and maintain code coverage standards.
