# MicroMind Quick Start Guide

This guide will help you get started with MicroMind, a .NET library for running tiny LLMs locally with automatic model management.

## Prerequisites

- .NET 9.0 SDK or later
- 4GB+ RAM (8GB recommended)
- ~2.5GB disk space for model cache
- Internet connection for initial model download

## Installation

### Option 1: NuGet Package (When Published)

```bash
dotnet add package MicroMind.Integration.AgentFramework
```

### Option 2: Project Reference

Add a project reference to your .csproj file:

```xml
<ItemGroup>
  <ProjectReference Include="path/to/MicroMind/src/MicroMind.Integration.AgentFramework/MicroMind.Integration.AgentFramework.csproj" />
</ItemGroup>
```

## Basic Setup

### 1. Create a Console Application

```bash
dotnet new console -n MyMicroMindApp
cd MyMicroMindApp
dotnet add package Microsoft.Extensions.Hosting
```

### 2. Configure Services

Update your `Program.cs`:

```csharp
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MicroMind.Integration.AgentFramework;

var builder = Host.CreateApplicationBuilder(args);

// Register MicroMind services with default configuration
builder.Services.AddMicroMind();

var host = builder.Build();

// Get the chat client
var chatClient = host.Services.GetRequiredService<IChatClient>();

// Your code here
```

### 3. Send Your First Message

```csharp
var response = await chatClient.CompleteAsync(
    [
        new ChatMessage(ChatRole.System, "You are a helpful assistant."),
        new ChatMessage(ChatRole.User, "What is 2 + 2?")
    ]);

Console.WriteLine($"Assistant: {response.Message.Text}");
```

## Configuration Options

### Using appsettings.json

Create an `appsettings.json` file:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "MicroMind": "Debug"
    }
  },
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
      "TopK": 40,
      "RepetitionPenalty": 1.1,
      "ContextSize": 4096,
      "GpuLayers": 0
    },
    "Download": {
      "MaxRetries": 3,
      "RetryDelayMs": 1000,
      "TimeoutSeconds": 300
    },
    "Cache": {
      "CachePath": null,
      "ValidationEnabled": true
    }
  }
}
```

Register with configuration:

```csharp
builder.Services.AddMicroMind(builder.Configuration);
```

### Using Code Configuration

```csharp
builder.Services.AddMicroMind(options =>
{
    // Inference settings
    options.Inference.Temperature = 0.8f;
    options.Inference.MaxTokens = 4096;
    options.Inference.TopP = 0.95f;
    options.Inference.GpuLayers = 0; // Set to > 0 for GPU acceleration
    
    // Cache settings
    options.Cache.CachePath = "/custom/cache/path";
    options.Cache.ValidationEnabled = true;
    
    // Download settings
    options.Download.MaxRetries = 5;
    options.Download.TimeoutSeconds = 600;
});
```

## Common Usage Patterns

### Simple Question-Answer

```csharp
var response = await chatClient.CompleteAsync(
    [
        new ChatMessage(ChatRole.User, "Explain quantum computing in simple terms.")
    ]);

Console.WriteLine(response.Message.Text);
```

### With System Prompt

```csharp
var response = await chatClient.CompleteAsync(
    [
        new ChatMessage(ChatRole.System, "You are a helpful coding assistant. Provide concise, accurate answers."),
        new ChatMessage(ChatRole.User, "How do I reverse a string in C#?")
    ]);

Console.WriteLine(response.Message.Text);
```

### Streaming Completions

```csharp
Console.Write("Assistant: ");

await foreach (var chunk in chatClient.CompleteStreamingAsync(
    [
        new ChatMessage(ChatRole.User, "Write a haiku about programming.")
    ]))
{
    Console.Write(chunk.Text);
}

Console.WriteLine();
```

### Conversation with History

```csharp
var messages = new List<ChatMessage>
{
    new(ChatRole.System, "You are a helpful assistant."),
    new(ChatRole.User, "What is the capital of France?"),
    new(ChatRole.Assistant, "The capital of France is Paris."),
    new(ChatRole.User, "What is its population?")
};

var response = await chatClient.CompleteAsync(messages);
Console.WriteLine(response.Message.Text);
```

### With Custom Options

```csharp
var options = new ChatOptions
{
    Temperature = 0.9f,
    MaxOutputTokens = 1000,
    TopP = 0.95f
};

var response = await chatClient.CompleteAsync(
    [
        new ChatMessage(ChatRole.User, "Write a creative story.")
    ],
    options);

Console.WriteLine(response.Message.Text);
```

## Understanding Model Download

On first use, MicroMind will:

1. Check if the model exists in the cache directory
2. If not found, download the model from the configured URL
3. Validate the downloaded file (if checksum is provided)
4. Cache the model for future use
5. Load the model into memory

The default cache location is:
- Windows: `%APPDATA%/MicroMind/models/`
- Linux/macOS: `~/.local/share/MicroMind/models/`

You can monitor download progress through logging:

```csharp
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);
```

## Performance Tips

### GPU Acceleration

If you have a compatible GPU, enable GPU layers:

```csharp
options.Inference.GpuLayers = 32; // Adjust based on your GPU memory
```

### Memory Management

The model stays loaded in memory after first use. To free memory:

```csharp
var runtime = host.Services.GetRequiredService<IInferenceRuntime>();
runtime.UnloadModel();
```

### Context Size

Adjust context size based on your needs:

```csharp
options.Inference.ContextSize = 2048; // Smaller = less memory, shorter context
options.Inference.ContextSize = 8192; // Larger = more memory, longer context
```

## Error Handling

```csharp
try
{
    var response = await chatClient.CompleteAsync(messages);
    Console.WriteLine(response.Message.Text);
}
catch (ModelDownloadException ex)
{
    Console.WriteLine($"Failed to download model: {ex.Message}");
}
catch (ModelLoadException ex)
{
    Console.WriteLine($"Failed to load model: {ex.Message}");
}
catch (MicroMindException ex)
{
    Console.WriteLine($"MicroMind error: {ex.Message}");
}
```

## Troubleshooting

### Model Download Fails

- Check your internet connection
- Verify the model URL is accessible
- Check disk space in cache directory
- Review logs for detailed error messages

### Out of Memory

- Reduce `ContextSize` in configuration
- Reduce `MaxTokens` for completions
- Ensure sufficient RAM (8GB+ recommended)
- Close other memory-intensive applications

### Slow Inference

- Enable GPU acceleration if available
- Use a smaller model
- Reduce `MaxTokens` for faster responses
- Consider using streaming for better perceived performance

### Model Not Loading

- Verify model file is not corrupted (check file size)
- Ensure model format is compatible (GGUF)
- Check file permissions in cache directory
- Try invalidating cache and re-downloading

## Next Steps

- Read the [Architecture Documentation](Architecture.md) to understand the internal design
- Explore the sample applications in the `samples/` directory
- Review the API documentation for advanced features
- Check out the test projects for more usage examples

## Support

For issues, questions, or contributions, please visit the project repository.
