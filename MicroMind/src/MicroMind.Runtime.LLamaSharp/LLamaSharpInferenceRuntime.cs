using System.Runtime.CompilerServices;
using LLama;
using LLama.Common;
using LLama.Sampling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MicroMind.Core.Abstractions;
using MicroMind.Core.Configuration;
using MicroMind.Core.Exceptions;
using MicroMind.Core.Models;

namespace MicroMind.Runtime.LLamaSharp;

public class LLamaSharpInferenceRuntime : IInferenceRuntime
{
    private readonly IOptions<MicroMindOptions> _options;
    private readonly ILogger<LLamaSharpInferenceRuntime> _logger;
    private LLamaWeights? _model;
    private LLamaContext? _context;
    private readonly SemaphoreSlim _inferenceLock = new(1, 1);

    public LLamaSharpInferenceRuntime(
        IOptions<MicroMindOptions> options,
        ILogger<LLamaSharpInferenceRuntime> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsModelLoaded => _model != null && _context != null;

    public async Task LoadModelAsync(string modelPath, CancellationToken cancellationToken = default)
    {
        if (IsModelLoaded)
        {
            _logger.LogInformation("Model is already loaded");
            return;
        }

        await _inferenceLock.WaitAsync(cancellationToken);
        try
        {
            if (IsModelLoaded)
            {
                return;
            }

            if (!File.Exists(modelPath))
            {
                throw new FileNotFoundException($"Model file not found: {modelPath}", modelPath);
            }

            _logger.LogInformation("Loading model from {ModelPath}", modelPath);

            var parameters = new ModelParams(modelPath)
            {
                ContextSize = (uint)_options.Value.Inference.ContextSize,
                GpuLayerCount = _options.Value.Inference.GpuLayers
            };

            try
            {
                _model = await Task.Run(() => LLamaWeights.LoadFromFile(parameters), cancellationToken);
                _context = await Task.Run(() => _model.CreateContext(parameters), cancellationToken);

                _logger.LogInformation("Model loaded successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load model from {ModelPath}", modelPath);
                throw new ModelLoadException($"Failed to load model from {modelPath}. The model file may be corrupted or incompatible.", ex);
            }
        }
        finally
        {
            _inferenceLock.Release();
        }
    }

    public async Task<CompletionResponse> GenerateCompletionAsync(
        CompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!IsModelLoaded)
        {
            throw new InvalidOperationException("Model is not loaded. Call LoadModelAsync first.");
        }

        await _inferenceLock.WaitAsync(cancellationToken);
        try
        {
            var prompt = BuildPrompt(request);
            var inferenceParams = CreateInferenceParams(request);

            _logger.LogDebug("Generating completion for prompt of length {PromptLength}", prompt.Length);

            var executor = new InteractiveExecutor(_context!);
            
            var completionText = new System.Text.StringBuilder();
            var tokenCount = 0;

            await foreach (var token in executor.InferAsync(prompt, inferenceParams, cancellationToken))
            {
                completionText.Append(token);
                tokenCount++;

                if (tokenCount >= request.MaxTokens)
                {
                    break;
                }
            }

            var result = completionText.ToString();
            var promptTokens = _context!.Tokenize(prompt).Length;
            
            _logger.LogDebug("Generated completion of length {CompletionLength}", result.Length);

            return new CompletionResponse
            {
                Text = result,
                PromptTokens = promptTokens,
                CompletionTokens = tokenCount,
                FinishReason = tokenCount >= request.MaxTokens ? FinishReason.Length : FinishReason.Stop
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Completion generation was cancelled");
            return new CompletionResponse
            {
                Text = string.Empty,
                PromptTokens = 0,
                CompletionTokens = 0,
                FinishReason = FinishReason.Cancelled
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during completion generation");
            throw new MicroMindException("Error during completion generation", ex);
        }
        finally
        {
            _inferenceLock.Release();
        }
    }

    public async IAsyncEnumerable<CompletionChunk> GenerateStreamingCompletionAsync(
        CompletionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!IsModelLoaded)
        {
            throw new InvalidOperationException("Model is not loaded. Call LoadModelAsync first.");
        }

        await _inferenceLock.WaitAsync(cancellationToken);
        try
        {
            var prompt = BuildPrompt(request);
            var inferenceParams = CreateInferenceParams(request);

            _logger.LogDebug("Generating streaming completion for prompt of length {PromptLength}", prompt.Length);

            var executor = new InteractiveExecutor(_context!);
            
            var tokenCount = 0;

            await foreach (var token in executor.InferAsync(prompt, inferenceParams, cancellationToken))
            {
                tokenCount++;
                var isFinal = tokenCount >= request.MaxTokens;

                yield return new CompletionChunk
                {
                    Text = token,
                    IsFinal = isFinal,
                    FinishReason = isFinal ? FinishReason.Length : null
                };

                if (isFinal)
                {
                    break;
                }
            }

            if (tokenCount < request.MaxTokens)
            {
                yield return new CompletionChunk
                {
                    Text = string.Empty,
                    IsFinal = true,
                    FinishReason = FinishReason.Stop
                };
            }
        }
        finally
        {
            _inferenceLock.Release();
        }
    }

    public void UnloadModel()
    {
        _inferenceLock.Wait();
        try
        {
            if (_context != null)
            {
                _context.Dispose();
                _context = null;
            }

            if (_model != null)
            {
                _model.Dispose();
                _model = null;
            }

            _logger.LogInformation("Model unloaded successfully");
        }
        finally
        {
            _inferenceLock.Release();
        }
    }

    private string BuildPrompt(CompletionRequest request)
    {
        var promptBuilder = new System.Text.StringBuilder();

        if (!string.IsNullOrEmpty(request.SystemPrompt))
        {
            promptBuilder.AppendLine($"<|system|>");
            promptBuilder.AppendLine(request.SystemPrompt);
            promptBuilder.AppendLine("<|end|>");
        }

        foreach (var message in request.History)
        {
            var role = message.Role switch
            {
                MessageRole.System => "system",
                MessageRole.User => "user",
                MessageRole.Assistant => "assistant",
                _ => "user"
            };

            promptBuilder.AppendLine($"<|{role}|>");
            promptBuilder.AppendLine(message.Content);
            promptBuilder.AppendLine("<|end|>");
        }

        promptBuilder.AppendLine("<|user|>");
        promptBuilder.AppendLine(request.UserPrompt);
        promptBuilder.AppendLine("<|end|>");
        promptBuilder.Append("<|assistant|>");

        return promptBuilder.ToString();
    }

    private InferenceParams CreateInferenceParams(CompletionRequest request)
    {
        var samplingPipeline = new DefaultSamplingPipeline
        {
            Temperature = request.Temperature,
            TopP = request.TopP,
            TopK = request.TopK ?? 40,
            RepeatPenalty = request.RepetitionPenalty
        };

        return new InferenceParams
        {
            MaxTokens = request.MaxTokens,
            SamplingPipeline = samplingPipeline,
            AntiPrompts = new List<string> { "<|end|>", "<|user|>" }
        };
    }

    public void Dispose()
    {
        UnloadModel();
        _inferenceLock.Dispose();
    }
}
