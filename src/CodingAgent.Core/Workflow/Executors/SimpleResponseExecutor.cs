using CodingAgent.Core.Models.Orchestration;
using Microsoft.Agents.AI.Workflows;

namespace CodingAgent.Core.Workflow.Executors;

/// <summary>
/// Executor that handles simple responses (greetings, unclear requests).
/// No agent needed - just returns canned responses.
/// </summary>
public sealed class SimpleResponseExecutor : Executor<IntentClassificationResult, SummaryResult>
{
    private readonly ILogger<SimpleResponseExecutor> _logger;

    public SimpleResponseExecutor(
        ILogger<SimpleResponseExecutor> logger,
        ExecutorOptions? options = null,
        bool declareCrossRunShareable = false)
        : base(nameof(SimpleResponseExecutor), options, declareCrossRunShareable)
    {
        _logger = logger;
    }

    public override ValueTask<SummaryResult> HandleAsync(
        IntentClassificationResult message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var response = message.Intent switch
        {
            IntentType.Greeting => "Hello! I'm ready to help with your coding tasks. What would you like me to do?",
            IntentType.Unclear => "I'm not sure what you'd like me to do. Could you please clarify your request?",
            _ => "I'm here to help. Please provide more details about what you need."
        };

        return ValueTask.FromResult(new SummaryResult
        {
            Summary = response,
            Metrics = new SummaryMetrics { StepsCompleted = 0, StepsTotal = 0, SuccessRate = "100%" }
        });
    }
}
