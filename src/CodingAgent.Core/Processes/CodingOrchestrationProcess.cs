#pragma warning disable SKEXP0080 // SK Process Framework is experimental

using CodingAgent.Models.Orchestration;
using CodingAgent.Processes.Steps;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Process;

namespace CodingAgent.Processes;

public static class CodingOrchestrationProcess
{
    public static KernelProcess BuildProcess()
    {
        var processBuilder = new ProcessBuilder("CodingOrchestrationProcess");

        // Add all process steps
        var intentStep = processBuilder.AddStepFromType<IntentClassifierStep>("IntentClassifier");
        var contextStep = processBuilder.AddStepFromType<ContextAgentStep>("ContextAgent");
        var researchStep = processBuilder.AddStepFromType<ResearchAgentStep>("ResearchAgent");
        var planningStep = processBuilder.AddStepFromType<PlanningAgentStep>("PlanningAgent");
        var executionStep = processBuilder.AddStepFromType<ExecutionAgentStep>("ExecutionAgent");
        var summaryStep = processBuilder.AddStepFromType<SummaryAgentStep>("SummaryAgent");

        // Start event routes to IntentClassifier
        processBuilder
            .OnInputEvent("Start")
            .SendEventTo(new(intentStep, functionName: IntentClassifierStep.Functions.ClassifyIntent));

        // Event routing from IntentClassifier
        intentStep
            .OnEvent(IntentClassifierStep.OutputEvents.QuestionDetected)
            .SendEventTo(new(researchStep, functionName: ResearchAgentStep.Functions.Answer));

        // For tasks, go through ContextAgent first to build enriched context
        intentStep
            .OnEvent(IntentClassifierStep.OutputEvents.TaskDetected)
            .SendEventTo(new(contextStep, functionName: ContextAgentStep.Functions.BuildContext));

        // ContextAgent builds enriched context and sends to PlanningAgent
        contextStep
            .OnEvent(ContextAgentStep.OutputEvents.ContextReady)
            .SendEventTo(new(planningStep, functionName: PlanningAgentStep.Functions.CreatePlan));

        // Event routing from ResearchAgent to Summary
        researchStep
            .OnEvent(ResearchAgentStep.OutputEvents.AnswerReady)
            .SendEventTo(new(summaryStep, functionName: SummaryAgentStep.Functions.SummarizeResearch));

        // Event routing from PlanningAgent to ExecutionAgent
        planningStep
            .OnEvent(PlanningAgentStep.OutputEvents.PlanReady)
            .SendEventTo(new(executionStep, functionName: ExecutionAgentStep.Functions.ExecutePlan));

        // Event routing from ExecutionAgent to Summary
        executionStep
            .OnEvent(ExecutionAgentStep.OutputEvents.PlanCompleted)
            .SendEventTo(new(summaryStep, functionName: SummaryAgentStep.Functions.SummarizeTask));

        return processBuilder.Build();
    }
}
