using CodingAgent.Core.Configuration;
using CodingAgent.Core.Models.Orchestration;
using CodingAgent.Core.Plugins;
using CodingAgent.Core.Workflow.Executors;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Options;

namespace CodingAgent.Core.Workflow;

/// <summary>
/// Builds the coding agent workflow with branching logic based on intent classification.
/// </summary>
public class CodingAgentWorkflowBuilder
{
    private readonly ModelSettings _modelSettings;
    private readonly AgentSettings _agentSettings;
    private readonly FileOperationsPlugin _fileOperations;
    private readonly CodeNavigationPlugin _codeNavigation;
    private readonly GitPlugin _git;
    private readonly CommandPlugin _command;
    private readonly ILoggerFactory _loggerFactory;

    public CodingAgentWorkflowBuilder(
        ModelSettings modelSettings,
        IOptions<AgentSettings> agentSettings,
        FileOperationsPlugin fileOperations,
        CodeNavigationPlugin codeNavigation,
        GitPlugin git,
        CommandPlugin command,
        ILoggerFactory loggerFactory)
    {
        _modelSettings = modelSettings;
        _agentSettings = agentSettings.Value;
        _fileOperations = fileOperations;
        _codeNavigation = codeNavigation;
        _git = git;
        _command = command;
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// Builds the workflow with the following structure:
    ///
    /// Input -> IntentClassifier -> [Branch based on Intent]
    ///   - Question -> Research -> Output
    ///   - Task -> Planning -> Implementation -> Output
    ///   - Greeting/Unclear -> SimpleResponse -> Output
    /// </summary>
    public Microsoft.Agents.AI.Workflows.Workflow Build()
    {
        var contextDiscoveryExecutor = new ContextDiscoveryExecutor(
            _modelSettings,
            _loggerFactory.CreateLogger<ContextDiscoveryExecutor>(),
            _fileOperations,
            _codeNavigation,
            _git);
        
        // Create executors
        var intentClassifier = new IntentClassifierExecutor(
            _modelSettings,
            _loggerFactory.CreateLogger<IntentClassifierExecutor>());

        var researchExecutor = new ResearchExecutor(
            _modelSettings,
            _agentSettings,
            _fileOperations,
            _codeNavigation,
            _git,
            _loggerFactory.CreateLogger<ResearchExecutor>());

        var planningExecutor = new PlanningExecutor(
            _modelSettings,
            _fileOperations,
            _codeNavigation,
            _git,
            _command,
            _loggerFactory.CreateLogger<PlanningExecutor>());

        var implementationExecutor = new ImplementationExecutor(
            _modelSettings,
            _agentSettings,
            _fileOperations,
            _codeNavigation,
            _git,
            _command,
            _loggerFactory.CreateLogger<ImplementationExecutor>());

        var simpleResponseExecutor = new SimpleResponseExecutor();

        // Build workflow with branching based on intent
        var builder = new WorkflowBuilder(contextDiscoveryExecutor);

        builder.AddEdge(contextDiscoveryExecutor, intentClassifier);

        // Add switch for intent-based routing
        builder.AddSwitch(intentClassifier, switchBuilder =>
            switchBuilder
                .AddCase(GetIntentCondition(IntentType.Question), researchExecutor)
                .AddCase(GetIntentCondition(IntentType.Task), planningExecutor)
                .WithDefault(simpleResponseExecutor)
        );

        // Planning leads to Implementation
        builder.AddEdge(planningExecutor, implementationExecutor);

        // Define outputs from all terminal executors
        builder.WithOutputFrom(researchExecutor, implementationExecutor, simpleResponseExecutor);

        return builder.Build();
    }

    private static Func<object?, bool> GetIntentCondition(IntentType expectedIntent)
    {
        return result => result is IntentClassificationResult intentResult && intentResult.Intent == expectedIntent;
    }
}
