using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using MicroMind.Core.Abstractions;
using MicroMind.Core.Models;

namespace MicroMind.Integration.AgentFramework;

public class MicroMindChatClient : IChatClient
{
    private readonly IModelManager _modelManager;
    private readonly IInferenceRuntime _inferenceRuntime;
    private readonly ILogger<MicroMindChatClient> _logger;
    private bool _isInitialized;

    public MicroMindChatClient(
        IModelManager modelManager,
        IInferenceRuntime inferenceRuntime,
        ILogger<MicroMindChatClient> logger)
    {
        _modelManager = modelManager ?? throw new ArgumentNullException(nameof(modelManager));
        _inferenceRuntime = inferenceRuntime ?? throw new ArgumentNullException(nameof(inferenceRuntime));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ChatClientMetadata Metadata => new("MicroMind", providerUri: null, modelId: _modelManager.GetMetadata().Name);

    public async Task<ChatCompletion> CompleteAsync(
        IList<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chatMessages);

        await EnsureInitializedAsync(cancellationToken);

        var request = ConvertToCompletionRequest(chatMessages, options);
        var response = await _inferenceRuntime.GenerateCompletionAsync(request, cancellationToken);

        return new ChatCompletion(new ChatMessage(ChatRole.Assistant, response.Text))
        {
            CompletionId = Guid.NewGuid().ToString(),
            ModelId = _modelManager.GetMetadata().Name,
            FinishReason = ConvertFinishReason(response.FinishReason),
            Usage = new UsageDetails
            {
                InputTokenCount = response.PromptTokens,
                OutputTokenCount = response.CompletionTokens,
                TotalTokenCount = response.TotalTokens
            }
        };
    }

    public async IAsyncEnumerable<StreamingChatCompletionUpdate> CompleteStreamingAsync(
        IList<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chatMessages);

        await EnsureInitializedAsync(cancellationToken);

        var request = ConvertToCompletionRequest(chatMessages, options);
        var completionId = Guid.NewGuid().ToString();
        var modelId = _modelManager.GetMetadata().Name;

        await foreach (var chunk in _inferenceRuntime.GenerateStreamingCompletionAsync(request, cancellationToken))
        {
            yield return new StreamingChatCompletionUpdate
            {
                CompletionId = completionId,
                Contents = [new TextContent(chunk.Text)],
                Role = ChatRole.Assistant,
                FinishReason = chunk.IsFinal ? ConvertFinishReason(chunk.FinishReason ?? Core.Models.FinishReason.Stop) : null
            };
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_isInitialized)
        {
            return;
        }

        _logger.LogInformation("Initializing MicroMind chat client...");

        await _modelManager.EnsureModelAvailableAsync(cancellationToken);

        var modelPath = await _modelManager.GetModelPathAsync(cancellationToken);
        await _inferenceRuntime.LoadModelAsync(modelPath, cancellationToken);

        _isInitialized = true;
        _logger.LogInformation("MicroMind chat client initialized successfully");
    }

    private CompletionRequest ConvertToCompletionRequest(IList<ChatMessage> chatMessages, ChatOptions? options)
    {
        var history = new List<ConversationMessage>();
        string? systemPrompt = null;
        string userPrompt = string.Empty;

        foreach (var message in chatMessages)
        {
            var content = string.Join("\n", message.Contents.OfType<TextContent>().Select(c => c.Text));

            if (message.Role == ChatRole.System)
            {
                systemPrompt = content;
            }
            else if (message.Role == ChatRole.User)
            {
                userPrompt = content;
            }
            else if (message.Role == ChatRole.Assistant)
            {
                history.Add(new ConversationMessage
                {
                    Role = MessageRole.Assistant,
                    Content = content
                });
            }
        }

        return new CompletionRequest
        {
            SystemPrompt = systemPrompt,
            UserPrompt = userPrompt,
            History = history,
            Temperature = options?.Temperature ?? 0.7f,
            MaxTokens = options?.MaxOutputTokens ?? 2048,
            TopP = options?.TopP ?? 0.95f
        };
    }

    private static ChatFinishReason? ConvertFinishReason(Core.Models.FinishReason finishReason)
    {
        return finishReason switch
        {
            Core.Models.FinishReason.Stop => ChatFinishReason.Stop,
            Core.Models.FinishReason.Length => ChatFinishReason.Length,
            Core.Models.FinishReason.Cancelled => ChatFinishReason.Stop,
            Core.Models.FinishReason.Error => ChatFinishReason.Stop,
            _ => ChatFinishReason.Stop
        };
    }
}
