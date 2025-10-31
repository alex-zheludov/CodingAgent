﻿using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MicroMind.Integration.AgentFramework;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

builder.Services.AddMicroMind();

var host = builder.Build();

var chatClient = host.Services.GetRequiredService<IChatClient>();
var logger = host.Services.GetRequiredService<ILogger<Program>>();

logger.LogInformation("MicroMind Console Sample Application");
logger.LogInformation("=====================================");
logger.LogInformation("");

try
{
    logger.LogInformation("Example 1: Simple Completion");
    logger.LogInformation("----------------------------");
    
    var messages = new List<ChatMessage>
    {
        new(ChatRole.System, "You are a helpful AI assistant."),
        new(ChatRole.User, "What is the capital of France?")
    };

    logger.LogInformation("Sending message: What is the capital of France?");
    var response = await chatClient.CompleteAsync(messages);
    logger.LogInformation("Response: {Response}", response.Message.Text);
    logger.LogInformation("");

    logger.LogInformation("Example 2: Streaming Completion");
    logger.LogInformation("--------------------------------");
    
    var streamingMessages = new List<ChatMessage>
    {
        new(ChatRole.System, "You are a helpful AI assistant."),
        new(ChatRole.User, "Write a short poem about coding.")
    };

    logger.LogInformation("Sending message: Write a short poem about coding.");
    logger.LogInformation("Streaming response:");
    
    await foreach (var chunk in chatClient.CompleteStreamingAsync(streamingMessages))
    {
        if (chunk.Contents.Count > 0)
        {
            var text = string.Join("", chunk.Contents.OfType<TextContent>().Select(c => c.Text));
            Console.Write(text);
        }
    }
    
    Console.WriteLine();
    logger.LogInformation("");

    logger.LogInformation("Example 3: Conversation with History");
    logger.LogInformation("-------------------------------------");
    
    var conversation = new List<ChatMessage>
    {
        new(ChatRole.System, "You are a helpful AI assistant."),
        new(ChatRole.User, "My name is Alice."),
        new(ChatRole.Assistant, "Hello Alice! Nice to meet you."),
        new(ChatRole.User, "What is my name?")
    };

    logger.LogInformation("Sending message: What is my name?");
    var conversationResponse = await chatClient.CompleteAsync(conversation);
    logger.LogInformation("Response: {Response}", conversationResponse.Message.Text);
    logger.LogInformation("");

    logger.LogInformation("All examples completed successfully!");
}
catch (Exception ex)
{
    logger.LogError(ex, "An error occurred while running the sample application");
    return 1;
}

return 0;
