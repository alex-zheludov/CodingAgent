# Multi-Agent Orchestration Requirements
## Phase 2: Semantic Kernel Process Framework Implementation

**Document Version:** 1.0
**Date:** 2025-10-20
**Status:** Requirements Definition

---

## 1. Executive Summary

This document outlines the requirements for transforming the current single-agent architecture into a multi-agent orchestration system using Semantic Kernel's Process Framework. The new architecture will introduce specialized agents optimized for different tasks (intent classification, Q&A, planning, execution) with appropriate model selection for each capability.

### Goals
- Reduce operational costs by 60-80% through intelligent model selection
- Improve response accuracy with specialized agents
- Enable explicit planning for complex tasks
- Support large context windows (2M tokens) for codebase Q&A
- Maintain backward compatibility with existing plugins and workflows

### Success Metrics
- Intent classification accuracy > 95%
- Average response time reduction by 30%
- Cost per request reduced by 60%+
- Plan execution success rate > 90%

---

## 2. Architecture Overview

### 2.1 Current State
- Single `CodingCodingAgent` handles all requests
- Single model (GPT-4o) for all operations
- Implicit planning through system prompts
- 20-iteration hard limit with completion markers
- Auto function calling via `ToolCallBehavior.AutoInvokeKernelFunctions`

**Reference Files:**
- [src/CodingAgent/Services/CodingAgent.cs](../src/CodingAgent/Services/CodingAgent.cs)
- [src/CodingAgent/Configuration/AzureOpenAISettings.cs](../src/CodingAgent/Configuration/AzureOpenAISettings.cs)

### 2.2 Target State
- Orchestration process with 4 specialized agent types
- Multi-model configuration (GPT-4o-mini, GPT-4o, Gemini 1.5 Pro)
- Explicit planning phase with structured output
- Plan-driven execution with step tracking
- Event-driven routing using Semantic Kernel Process Framework

---

## 3. Semantic Kernel Process Framework

### 3.1 Framework Overview
Semantic Kernel's Process Framework provides:
- **Process Builder API** - Define multi-step workflows
- **Steps** - Individual processing units (agents)
- **Events** - Trigger transitions between steps
- **State Management** - Persist process state across steps
- **Process Visualization** - Debug and monitor workflows

### 3.2 Key Concepts

#### Process
A workflow definition containing multiple steps and their relationships.

```csharp
var processBuilder = new ProcessBuilder("CodingOrchestrationProcess");
```

#### Step
An individual unit of work (maps to an agent).

```csharp
var intentStep = processBuilder.AddStepFromType<IntentClassifierStep>();
```

#### Event
Triggers that move the process between steps.

```csharp
intentStep.OnEvent("QuestionDetected").SendEventTo(qaStep);
intentStep.OnEvent("TaskDetected").SendEventTo(planningStep);
```

#### State
Shared context passed between steps.

```csharp
public class OrchestrationState
{
    public string OriginalInput { get; set; }
    public IntentType Intent { get; set; }
    public ExecutionPlan Plan { get; set; }
    public List<StepResult> ExecutionResults { get; set; }
    public WorkspaceContext WorkspaceContext { get; set; }
}
```

---

## 4. Agent Specifications

### 4.1 Intent Classifier Agent

**Purpose:** Rapidly classify incoming requests to route to appropriate specialist agent.

**Model Configuration:**
- **Provider:** Azure OpenAI
- **Model:** gpt-4o-mini
- **Max Tokens:** 500
- **Temperature:** 0.0 (deterministic)
- **Estimated Cost:** ~$0.0002 per request

**Input:**
```json
{
  "input": "How does the FileOpsPlugin validate file paths?",
  "workspaceContext": { /* repository info */ }
}
```

**Output:**
```json
{
  "intent": "QUESTION",
  "confidence": 0.95,
  "reasoning": "User is asking about existing code functionality",
  "suggestedAgent": "QAAgent"
}
```

**Classification Categories:**
1. **QUESTION** - Information seeking about existing code
   - Examples: "How does X work?", "Where is Y defined?", "What does Z do?"

2. **TASK** - Request for code changes/implementation
   - Examples: "Add feature X", "Fix bug Y", "Refactor Z"

3. **GREETING** - Casual conversation, status check
   - Examples: "Hello", "What's your status?", "Are you ready?"

4. **UNCLEAR** - Ambiguous, needs clarification
   - Examples: "Do something with the config", "Fix it"

**Performance Requirements:**
- Response time: < 500ms
- Accuracy: > 95%
- Confidence threshold: 0.8 (below this, escalate to human)

**Implementation Class:**
```csharp
public class IntentClassifierStep : KernelProcessStep<OrchestrationState>
{
    [KernelFunction]
    public async Task<IntentResult> ClassifyAsync(KernelProcessStepContext context, OrchestrationState state);
}
```

---

### 4.2 Q&A Agent

**Purpose:** Answer questions about existing codebase with large context window.

**Model Configuration:**
- **Provider:** Google AI
- **Model:** gemini-1.5-pro-002
- **Max Tokens:** 8192 (output)
- **Context Window:** 2,097,152 tokens (2M)
- **Temperature:** 0.3
- **Estimated Cost:** ~$0.01 per request

**Capabilities:**
- Load entire repository context (within 2M token limit)
- Understand relationships between files
- Provide code examples and explanations
- Reference specific line numbers and file paths

**Input:**
```json
{
  "question": "How does the SecurityService validate file paths?",
  "relevantFiles": [
    "src/CodingAgent/Services/SecurityService.cs",
    "src/CodingAgent/Plugins/FileOpsPlugin.cs"
  ],
  "workspaceContext": { /* full repo context */ }
}
```

**Output:**
```json
{
  "answer": "The SecurityService validates file paths in the ValidateFilePathAsync method...",
  "references": [
    {
      "file": "src/CodingAgent/Services/SecurityService.cs",
      "lineStart": 45,
      "lineEnd": 67,
      "snippet": "public async Task<(bool IsValid, string Error)> ValidateFilePathAsync..."
    }
  ],
  "confidence": 0.92
}
```

**Tools Available:**
- FileOpsPlugin (read-only operations)
- CodeNavPlugin (search, navigation)
- GitPlugin (read-only: status, diff, log)

**Performance Requirements:**
- Response time: < 10s
- Include file/line references in 100% of answers
- Accuracy validated through user feedback

**Implementation Class:**
```csharp
public class QAAgentStep : KernelProcessStep<OrchestrationState>
{
    private readonly Kernel _geminiKernel;
    private readonly IFileOpsPlugin _fileOps;
    private readonly ICodeNavPlugin _codeNav;

    [KernelFunction]
    public async Task<QAResult> AnswerAsync(KernelProcessStepContext context, OrchestrationState state);
}
```

---

### 4.3 Planning Agent

**Purpose:** Create detailed, structured execution plans for complex tasks.

**Model Configuration:**
- **Provider:** Azure OpenAI
- **Model:** gpt-4o
- **Max Tokens:** 4096
- **Temperature:** 0.2 (balanced)
- **Response Format:** JSON (structured output)
- **Estimated Cost:** ~$0.02 per request

**Capabilities:**
- Decompose complex tasks into sequential steps
- Identify file dependencies
- Determine required tools for each step
- Estimate execution complexity
- Validate plan feasibility

**Input:**
```json
{
  "task": "Add unit tests for SecurityService file path validation",
  "workspaceContext": { /* repo info */ },
  "constraints": {
    "maxSteps": 10,
    "allowedTools": ["FileOps", "Git", "Command"]
  }
}
```

**Output (Structured JSON):**
```json
{
  "planId": "plan-uuid-123",
  "task": "Add unit tests for SecurityService file path validation",
  "estimatedIterations": 5,
  "estimatedDuration": "5-10 minutes",
  "steps": [
    {
      "stepId": 1,
      "action": "Analyze SecurityService implementation",
      "description": "Read SecurityService.cs to understand validation logic",
      "tools": ["FileOps.ReadFile"],
      "targetFiles": ["src/CodingAgent/Services/SecurityService.cs"],
      "dependencies": [],
      "expectedOutcome": "Understanding of validation methods to test"
    },
    {
      "stepId": 2,
      "action": "Locate existing test structure",
      "description": "Find test project and identify testing patterns",
      "tools": ["FileOps.ListDirectory", "CodeNav.SearchCode"],
      "targetFiles": ["tests/**/*.cs"],
      "dependencies": [],
      "expectedOutcome": "Location of test project and naming conventions"
    },
    {
      "stepId": 3,
      "action": "Design test cases",
      "description": "Identify edge cases and scenarios to cover",
      "tools": [],
      "dependencies": [1],
      "expectedOutcome": "List of test cases (valid paths, traversal attempts, etc.)"
    },
    {
      "stepId": 4,
      "action": "Implement test class",
      "description": "Write SecurityServiceTests.cs with comprehensive coverage",
      "tools": ["FileOps.WriteFile"],
      "targetFiles": ["tests/Services/SecurityServiceTests.cs"],
      "dependencies": [2, 3],
      "expectedOutcome": "Complete test class with 10+ test methods"
    },
    {
      "stepId": 5,
      "action": "Run tests and verify",
      "description": "Execute dotnet test and confirm all pass",
      "tools": ["Command.TestDotnet"],
      "dependencies": [4],
      "expectedOutcome": "All tests pass, coverage report generated"
    }
  ],
  "risks": [
    {
      "description": "Test project may not exist",
      "mitigation": "Create test project structure if needed",
      "severity": "medium"
    }
  ],
  "requiredTools": ["FileOps", "CodeNav", "Command"],
  "confidence": 0.88
}
```

**Validation Rules:**
- Maximum 15 steps per plan
- All steps must have clear expected outcomes
- No circular dependencies
- All referenced tools must be available
- File paths must be within workspace

**Performance Requirements:**
- Response time: < 15s
- Plan validation success: 100%
- Steps must be executable by ExecutionAgent

**Implementation Class:**
```csharp
public class PlanningAgentStep : KernelProcessStep<OrchestrationState>
{
    private readonly Kernel _planningKernel;
    private readonly PlanValidator _validator;

    [KernelFunction]
    public async Task<ExecutionPlan> CreatePlanAsync(KernelProcessStepContext context, OrchestrationState state);

    private async Task<ValidationResult> ValidatePlanAsync(ExecutionPlan plan);
}
```

---

### 4.4 Execution Agent

**Purpose:** Execute plans step-by-step with focused context and tool usage.

**Model Configuration:**
- **Provider:** Azure OpenAI
- **Model:** gpt-4o
- **Max Tokens:** 4096
- **Temperature:** 0.3
- **Estimated Cost:** ~$0.02-0.10 per plan (varies by steps)

**Capabilities:**
- Execute single plan step with precision
- Use only tools specified in step
- Track step completion and outcomes
- Handle errors with retry logic
- Report progress in real-time

**Input (per step):**
```json
{
  "step": {
    "stepId": 4,
    "action": "Implement test class",
    "description": "Write SecurityServiceTests.cs with comprehensive coverage",
    "tools": ["FileOps.WriteFile"],
    "targetFiles": ["tests/Services/SecurityServiceTests.cs"],
    "dependencies": [2, 3],
    "expectedOutcome": "Complete test class with 10+ test methods"
  },
  "previousStepResults": [
    { "stepId": 2, "outcome": "Found test project at tests/CodingAgent.Tests" },
    { "stepId": 3, "outcome": "Identified 12 test cases to implement" }
  ],
  "planContext": { /* full plan */ }
}
```

**Output (per step):**
```json
{
  "stepId": 4,
  "status": "completed",
  "executionTime": "12.5s",
  "toolsUsed": [
    {
      "tool": "FileOps.WriteFile",
      "parameters": {
        "repositoryName": "TestRepo",
        "filePath": "tests/Services/SecurityServiceTests.cs"
      },
      "result": "success"
    }
  ],
  "outcome": "Created SecurityServiceTests.cs with 12 test methods covering all validation scenarios",
  "filesModified": ["tests/Services/SecurityServiceTests.cs"],
  "nextStepRecommendation": "Proceed to step 5 (run tests)",
  "confidence": 0.95
}
```

**Execution Strategy:**
- Execute steps sequentially (honor dependencies)
- Verify dependencies met before execution
- Load only relevant context (previous step results + current step)
- Use restricted tool set (only tools specified in step)
- Validate outcome against expected result
- Retry on transient failures (max 2 retries)

**Error Handling:**
```json
{
  "stepId": 5,
  "status": "failed",
  "error": {
    "type": "TestFailure",
    "message": "3 tests failed: TestValidatePath_WithTraversal",
    "toolInvolved": "Command.TestDotnet",
    "recoverable": true
  },
  "retryStrategy": "Fix failing tests in new step",
  "suggestedAction": "Insert new step 5a: Fix test implementation errors"
}
```

**Performance Requirements:**
- Step execution time: < 30s per step
- Success rate: > 90%
- Accurate outcome reporting: 100%

**Implementation Class:**
```csharp
public class ExecutionAgentStep : KernelProcessStep<OrchestrationState>
{
    private readonly Kernel _executionKernel;
    private readonly IEnumerable<IPlugin> _plugins;

    [KernelFunction]
    public async Task<StepResult> ExecuteStepAsync(
        KernelProcessStepContext context,
        OrchestrationState state,
        PlanStep step);

    private async Task<bool> ValidateDependenciesAsync(PlanStep step, List<StepResult> results);
    private async Task<StepResult> RetryStepAsync(PlanStep step, Exception error);
}
```

---

## 5. Process Flow Definition

### 5.1 Process Graph

```
                            ┌─────────────────┐
                            │  Start Process  │
                            └────────┬────────┘
                                     │
                                     ▼
                       ┌──────────────────────────┐
                       │  Intent Classifier Step  │
                       │  (gpt-4o-mini, <500ms)   │
                       └──────────┬───────────────┘
                                  │
                ┌─────────────────┼─────────────────┐
                │                 │                 │
         [QUESTION]           [TASK]           [GREETING/UNCLEAR]
                │                 │                 │
                ▼                 ▼                 ▼
      ┌──────────────┐  ┌──────────────┐  ┌──────────────┐
      │   Q&A Step   │  │ Planning Step│  │ Simple Reply │
      │  (Gemini)    │  │  (gpt-4o)    │  │   Step       │
      │              │  │              │  │              │
      │  - Read ops  │  │  - Analyze   │  │  - Greet     │
      │  - Search    │  │  - Decompose │  │  - Clarify   │
      │  - Explain   │  │  - Validate  │  └──────┬───────┘
      └──────┬───────┘  └──────┬───────┘         │
             │                 │                 │
             │                 ▼                 │
             │        ┌──────────────────┐       │
             │        │ Execution Loop   │       │
             │        │   (gpt-4o)       │       │
             │        │                  │       │
             │        │  For each step:  │       │
             │        │  - Load context  │       │
             │        │  - Execute tools │       │
             │        │  - Validate      │       │
             │        │  - Record result │       │
             │        └──────┬───────────┘       │
             │               │                   │
             └───────────────┼───────────────────┘
                             │
                             ▼
                   ┌──────────────────┐
                   │  End Process     │
                   │  - Save session  │
                   │  - Return result │
                   └──────────────────┘
```

### 5.2 Process Implementation

**File:** `src/CodingAgent/Processes/CodingOrchestrationProcess.cs`

```csharp
public class CodingOrchestrationProcess
{
    public static ProcessBuilder BuildProcess()
    {
        var processBuilder = new ProcessBuilder("CodingOrchestrationProcess");

        // Step 1: Intent Classification
        var intentStep = processBuilder
            .AddStepFromType<IntentClassifierStep>("IntentClassifier");

        // Step 2a: Q&A Handler
        var qaStep = processBuilder
            .AddStepFromType<QAAgentStep>("QAAgent");

        // Step 2b: Simple Reply Handler
        var simpleReplyStep = processBuilder
            .AddStepFromType<SimpleReplyStep>("SimpleReply");

        // Step 3: Planning
        var planningStep = processBuilder
            .AddStepFromType<PlanningAgentStep>("PlanningAgent");

        // Step 4: Execution Loop
        var executionStep = processBuilder
            .AddStepFromType<ExecutionAgentStep>("ExecutionAgent");

        // Step 5: Completion Handler
        var completionStep = processBuilder
            .AddStepFromType<CompletionStep>("Completion");

        // Define event routing
        intentStep
            .OnEvent("QuestionDetected")
            .SendEventTo(qaStep.WhereInputIs(nameof(OrchestrationState)));

        intentStep
            .OnEvent("TaskDetected")
            .SendEventTo(planningStep.WhereInputIs(nameof(OrchestrationState)));

        intentStep
            .OnEvent("GreetingDetected")
            .SendEventTo(simpleReplyStep.WhereInputIs(nameof(OrchestrationState)));

        intentStep
            .OnEvent("UnclearDetected")
            .SendEventTo(simpleReplyStep.WhereInputIs(nameof(OrchestrationState)));

        planningStep
            .OnEvent("PlanReady")
            .SendEventTo(executionStep.WhereInputIs(nameof(OrchestrationState)));

        executionStep
            .OnEvent("StepCompleted")
            .SendEventTo(executionStep.WhereInputIs(nameof(OrchestrationState))); // Loop

        executionStep
            .OnEvent("PlanCompleted")
            .SendEventTo(completionStep.WhereInputIs(nameof(OrchestrationState)));

        qaStep
            .OnEvent("AnswerReady")
            .SendEventTo(completionStep.WhereInputIs(nameof(OrchestrationState)));

        simpleReplyStep
            .OnEvent("ReplyReady")
            .SendEventTo(completionStep.WhereInputIs(nameof(OrchestrationState)));

        return processBuilder;
    }
}
```

### 5.3 State Management

**File:** `src/CodingAgent/Processes/OrchestrationState.cs`

```csharp
public class OrchestrationState
{
    // Input
    public string OriginalInput { get; set; }
    public string SessionId { get; set; }
    public WorkspaceContext WorkspaceContext { get; set; }

    // Intent Classification
    public IntentType? Intent { get; set; }
    public double IntentConfidence { get; set; }

    // Planning
    public ExecutionPlan Plan { get; set; }
    public int CurrentStepIndex { get; set; }

    // Execution
    public List<StepResult> StepResults { get; set; } = new();
    public List<string> ModifiedFiles { get; set; } = new();
    public List<ToolInvocation> ToolInvocations { get; set; } = new();

    // Output
    public string FinalResponse { get; set; }
    public AgentStatus Status { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }

    // Metrics
    public Dictionary<string, object> Metrics { get; set; } = new()
    {
        ["IntentClassificationTime"] = 0.0,
        ["PlanningTime"] = 0.0,
        ["ExecutionTime"] = 0.0,
        ["TotalTokensUsed"] = 0,
        ["TotalCost"] = 0.0
    };
}
```

---

## 6. Model Configuration Requirements

### 6.1 Configuration Schema

**File:** `src/CodingAgent/Configuration/ModelSettings.cs`

```csharp
public class ModelSettings
{
    public ModelConfig IntentClassifier { get; set; }
    public ModelConfig QuestionAnswering { get; set; }
    public ModelConfig Planning { get; set; }
    public ModelConfig Execution { get; set; }
}

public class ModelConfig
{
    // Provider: "AzureOpenAI" | "OpenAI" | "GoogleAI"
    public string Provider { get; set; }

    // Model identifier
    public string Model { get; set; }

    // API Configuration
    public string Endpoint { get; set; }
    public string ApiKey { get; set; }

    // Generation Parameters
    public int MaxTokens { get; set; }
    public decimal Temperature { get; set; }

    // Optional: Response format for structured output
    public string ResponseFormat { get; set; } // "text" | "json"

    // Optional: Fallback model if primary unavailable
    public string FallbackModel { get; set; }
}
```

### 6.2 appsettings.json Example

```json
{
  "Models": {
    "IntentClassifier": {
      "Provider": "AzureOpenAI",
      "Model": "gpt-4o-mini",
      "Endpoint": "https://aif-az-coding-agent.openai.azure.com/",
      "ApiKey": "",
      "MaxTokens": 500,
      "Temperature": 0.0,
      "ResponseFormat": "text"
    },
    "QuestionAnswering": {
      "Provider": "GoogleAI",
      "Model": "gemini-1.5-pro-002",
      "Endpoint": "https://generativelanguage.googleapis.com",
      "ApiKey": "",
      "MaxTokens": 8192,
      "Temperature": 0.3,
      "ResponseFormat": "text",
      "FallbackModel": "gpt-4o"
    },
    "Planning": {
      "Provider": "AzureOpenAI",
      "Model": "gpt-4o",
      "Endpoint": "https://aif-az-coding-agent.openai.azure.com/",
      "ApiKey": "",
      "MaxTokens": 4096,
      "Temperature": 0.2,
      "ResponseFormat": "json"
    },
    "Execution": {
      "Provider": "AzureOpenAI",
      "Model": "gpt-4o",
      "Endpoint": "https://aif-az-coding-agent.openai.azure.com/",
      "ApiKey": "",
      "MaxTokens": 4096,
      "Temperature": 0.3,
      "ResponseFormat": "text"
    }
  }
}
```

### 6.3 Kernel Factory

**File:** `src/CodingAgent/Services/KernelFactory.cs`

```csharp
public class KernelFactory
{
    private readonly ModelSettings _modelSettings;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IServiceProvider _serviceProvider;

    public Kernel CreateKernel(AgentCapability capability)
    {
        var config = capability switch
        {
            AgentCapability.IntentClassification => _modelSettings.IntentClassifier,
            AgentCapability.QuestionAnswering => _modelSettings.QuestionAnswering,
            AgentCapability.Planning => _modelSettings.Planning,
            AgentCapability.Execution => _modelSettings.Execution,
            _ => throw new ArgumentException($"Unknown capability: {capability}")
        };

        var builder = Kernel.CreateBuilder();

        // Add model connector based on provider
        switch (config.Provider)
        {
            case "AzureOpenAI":
                builder.AddAzureOpenAIChatCompletion(
                    deploymentName: config.Model,
                    endpoint: config.Endpoint,
                    apiKey: config.ApiKey);
                break;

            case "OpenAI":
                builder.AddOpenAIChatCompletion(
                    modelId: config.Model,
                    apiKey: config.ApiKey);
                break;

            case "GoogleAI":
                builder.AddGoogleAIGeminiChatCompletion(
                    modelId: config.Model,
                    apiKey: config.ApiKey);
                break;

            default:
                throw new ArgumentException($"Unknown provider: {config.Provider}");
        }

        // Add plugins based on capability
        RegisterPlugins(builder, capability);

        // Add logging
        builder.Services.AddSingleton(_loggerFactory);

        return builder.Build();
    }

    private void RegisterPlugins(IKernelBuilder builder, AgentCapability capability)
    {
        // Intent Classifier: No plugins needed
        if (capability == AgentCapability.IntentClassification)
            return;

        // Q&A Agent: Read-only plugins
        if (capability == AgentCapability.QuestionAnswering)
        {
            builder.Plugins.AddFromObject(_serviceProvider.GetRequiredService<FileOpsPlugin>(), "FileOps");
            builder.Plugins.AddFromObject(_serviceProvider.GetRequiredService<CodeNavPlugin>(), "CodeNav");
            builder.Plugins.AddFromObject(_serviceProvider.GetRequiredService<GitPlugin>(), "Git");
            return;
        }

        // Planning Agent: All plugins for analysis
        // Execution Agent: All plugins for operations
        if (capability == AgentCapability.Planning || capability == AgentCapability.Execution)
        {
            builder.Plugins.AddFromObject(_serviceProvider.GetRequiredService<FileOpsPlugin>(), "FileOps");
            builder.Plugins.AddFromObject(_serviceProvider.GetRequiredService<GitPlugin>(), "Git");
            builder.Plugins.AddFromObject(_serviceProvider.GetRequiredService<CommandPlugin>(), "Command");
            builder.Plugins.AddFromObject(_serviceProvider.GetRequiredService<CodeNavPlugin>(), "CodeNav");
        }
    }
}
```

---

## 7. Data Models

### 7.1 Intent Result

**File:** `src/CodingAgent/Models/IntentResult.cs`

```csharp
public class IntentResult
{
    public IntentType Intent { get; set; }
    public double Confidence { get; set; }
    public string Reasoning { get; set; }
    public string SuggestedAgent { get; set; }
}

public enum IntentType
{
    Question,
    Task,
    Greeting,
    Unclear
}
```

### 7.2 Execution Plan

**File:** `src/CodingAgent/Models/ExecutionPlan.cs`

```csharp
public class ExecutionPlan
{
    public string PlanId { get; set; }
    public string Task { get; set; }
    public int EstimatedIterations { get; set; }
    public string EstimatedDuration { get; set; }
    public List<PlanStep> Steps { get; set; }
    public List<PlanRisk> Risks { get; set; }
    public List<string> RequiredTools { get; set; }
    public double Confidence { get; set; }
}

public class PlanStep
{
    public int StepId { get; set; }
    public string Action { get; set; }
    public string Description { get; set; }
    public List<string> Tools { get; set; }
    public List<string> TargetFiles { get; set; }
    public List<int> Dependencies { get; set; }
    public string ExpectedOutcome { get; set; }
}

public class PlanRisk
{
    public string Description { get; set; }
    public string Mitigation { get; set; }
    public string Severity { get; set; } // "low" | "medium" | "high"
}
```

### 7.3 Step Result

**File:** `src/CodingAgent/Models/StepResult.cs`

```csharp
public class StepResult
{
    public int StepId { get; set; }
    public StepStatus Status { get; set; }
    public TimeSpan ExecutionTime { get; set; }
    public List<ToolInvocation> ToolsUsed { get; set; }
    public string Outcome { get; set; }
    public List<string> FilesModified { get; set; }
    public string NextStepRecommendation { get; set; }
    public double Confidence { get; set; }
    public StepError Error { get; set; }
}

public enum StepStatus
{
    Pending,
    InProgress,
    Completed,
    Failed,
    Skipped
}

public class ToolInvocation
{
    public string Tool { get; set; }
    public Dictionary<string, object> Parameters { get; set; }
    public string Result { get; set; }
    public TimeSpan ExecutionTime { get; set; }
}

public class StepError
{
    public string Type { get; set; }
    public string Message { get; set; }
    public string ToolInvolved { get; set; }
    public bool Recoverable { get; set; }
}
```

### 7.4 Q&A Result

**File:** `src/CodingAgent/Models/QAResult.cs`

```csharp
public class QAResult
{
    public string Answer { get; set; }
    public List<CodeReference> References { get; set; }
    public double Confidence { get; set; }
}

public class CodeReference
{
    public string File { get; set; }
    public int LineStart { get; set; }
    public int LineEnd { get; set; }
    public string Snippet { get; set; }
}
```

---

## 8. Service Integration

### 8.1 Orchestration Service

**File:** `src/CodingAgent/Services/OrchestrationService.cs`

```csharp
public interface IOrchestrationService
{
    Task<OrchestrationResult> ProcessRequestAsync(string input, string sessionId);
    Task<OrchestrationState> GetStateAsync(string sessionId);
    Task<bool> CancelProcessAsync(string sessionId);
}

public class OrchestrationService : IOrchestrationService
{
    private readonly KernelProcessFactory _processFactory;
    private readonly ISessionStore _sessionStore;
    private readonly WorkspaceContext _workspaceContext;
    private readonly ILogger<OrchestrationService> _logger;

    public async Task<OrchestrationResult> ProcessRequestAsync(string input, string sessionId)
    {
        // 1. Initialize state
        var state = new OrchestrationState
        {
            OriginalInput = input,
            SessionId = sessionId,
            WorkspaceContext = _workspaceContext,
            StartTime = DateTime.UtcNow,
            Status = AgentStatus.Working
        };

        // 2. Build process
        var process = _processFactory.CreateProcess();

        // 3. Execute process
        var result = await process.ExecuteAsync(state);

        // 4. Save to session store
        await _sessionStore.SaveStateAsync(sessionId, state);

        // 5. Return result
        return new OrchestrationResult
        {
            Response = state.FinalResponse,
            Status = state.Status,
            Metrics = state.Metrics
        };
    }
}
```

### 8.2 Plan Validator

**File:** `src/CodingAgent/Services/PlanValidator.cs`

```csharp
public class PlanValidator
{
    private readonly SecurityService _securityService;
    private readonly IEnumerable<IPlugin> _availablePlugins;

    public ValidationResult Validate(ExecutionPlan plan)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        // 1. Check step count
        if (plan.Steps.Count > 15)
            errors.Add("Plan exceeds maximum 15 steps");

        // 2. Check for circular dependencies
        if (HasCircularDependencies(plan.Steps))
            errors.Add("Plan contains circular dependencies");

        // 3. Verify all tools exist
        foreach (var step in plan.Steps)
        {
            foreach (var tool in step.Tools)
            {
                if (!IsToolAvailable(tool))
                    errors.Add($"Tool '{tool}' not available for step {step.StepId}");
            }
        }

        // 4. Validate file paths
        foreach (var step in plan.Steps)
        {
            foreach (var file in step.TargetFiles)
            {
                var validation = _securityService.ValidateFilePathAsync(file, "TestRepo").Result;
                if (!validation.IsValid)
                    errors.Add($"Invalid file path in step {step.StepId}: {validation.Error}");
            }
        }

        // 5. Check dependencies are valid
        foreach (var step in plan.Steps)
        {
            foreach (var dep in step.Dependencies)
            {
                if (dep >= step.StepId)
                    errors.Add($"Step {step.StepId} depends on future step {dep}");
                if (!plan.Steps.Any(s => s.StepId == dep))
                    errors.Add($"Step {step.StepId} depends on non-existent step {dep}");
            }
        }

        return new ValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors,
            Warnings = warnings
        };
    }

    private bool HasCircularDependencies(List<PlanStep> steps)
    {
        // Implement topological sort to detect cycles
        // Return true if cycle detected
    }

    private bool IsToolAvailable(string toolName)
    {
        // Check if tool exists in available plugins
    }
}
```

---

## 9. API Changes

### 9.1 New Endpoints

**File:** `src/CodingAgent/Endpoints/OrchestrationEndpoints.cs`

```csharp
public static class OrchestrationEndpoints
{
    public static void MapOrchestrationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v2/orchestration");

        // Execute with orchestration
        group.MapPost("/execute", async (
            [FromBody] ExecuteRequest request,
            IOrchestrationService orchestrationService) =>
        {
            var result = await orchestrationService.ProcessRequestAsync(
                request.Input,
                request.SessionId ?? Guid.NewGuid().ToString());

            return Results.Ok(result);
        });

        // Get orchestration state
        group.MapGet("/state/{sessionId}", async (
            string sessionId,
            IOrchestrationService orchestrationService) =>
        {
            var state = await orchestrationService.GetStateAsync(sessionId);
            return Results.Ok(state);
        });

        // Get execution plan
        group.MapGet("/plan/{sessionId}", async (
            string sessionId,
            ISessionStore sessionStore) =>
        {
            var state = await sessionStore.GetStateAsync<OrchestrationState>(sessionId);
            return Results.Ok(state?.Plan);
        });

        // Get step results
        group.MapGet("/steps/{sessionId}", async (
            string sessionId,
            ISessionStore sessionStore) =>
        {
            var state = await sessionStore.GetStateAsync<OrchestrationState>(sessionId);
            return Results.Ok(state?.StepResults);
        });

        // Cancel execution
        group.MapPost("/cancel/{sessionId}", async (
            string sessionId,
            IOrchestrationService orchestrationService) =>
        {
            var cancelled = await orchestrationService.CancelProcessAsync(sessionId);
            return Results.Ok(new { Cancelled = cancelled });
        });
    }
}
```

### 9.2 Backward Compatibility

- Keep existing `/api/execute` endpoint for v1 (single-agent)
- Add `/api/v2/orchestration/execute` for new multi-agent flow
- Allow configuration flag to switch default behavior
- Gradually migrate clients to v2

**Configuration:**
```json
{
  "Orchestration": {
    "Enabled": true,
    "DefaultToV2": false,  // false = use v1 by default
    "AllowV1Fallback": true
  }
}
```

---

## 10. Testing Requirements

### 10.1 Unit Tests

**Intent Classifier Tests:**
- Accuracy on 100+ labeled examples
- Confidence threshold validation
- Edge case handling (empty input, very long input)
- Multi-language input

**Planning Agent Tests:**
- Plan generation for common tasks
- Validation rule enforcement
- Dependency graph correctness
- Tool availability checking

**Execution Agent Tests:**
- Step execution success
- Error handling and retry logic
- Tool invocation correctness
- State persistence

**Q&A Agent Tests:**
- Accuracy on codebase questions
- Reference quality (file/line numbers)
- Context window utilization
- Fallback behavior

### 10.2 Integration Tests

**End-to-End Orchestration:**
```csharp
[Fact]
public async Task OrchestrationFlow_Question_ReturnsAccurateAnswer()
{
    // Arrange
    var input = "How does the SecurityService validate file paths?";

    // Act
    var result = await _orchestrationService.ProcessRequestAsync(input, "test-session");

    // Assert
    Assert.Equal(AgentStatus.Complete, result.Status);
    Assert.Contains("ValidateFilePathAsync", result.Response);
    Assert.NotEmpty(result.References);
}

[Fact]
public async Task OrchestrationFlow_Task_ExecutesPlanSuccessfully()
{
    // Arrange
    var input = "Add a new method to SecurityService that validates file extensions";

    // Act
    var result = await _orchestrationService.ProcessRequestAsync(input, "test-session");

    // Assert
    Assert.Equal(AgentStatus.Complete, result.Status);
    Assert.True(result.Metrics["EstimatedSteps"] > 0);
    Assert.All(result.StepResults, r => Assert.Equal(StepStatus.Completed, r.Status));
}
```

### 10.3 Performance Tests

**Benchmarks:**
- Intent classification: < 500ms (p95)
- Q&A response: < 10s (p95)
- Planning: < 15s (p95)
- Step execution: < 30s per step (p95)
- Full task completion: < 5 minutes (p95)

**Load Tests:**
- 10 concurrent sessions
- Sustained throughput: 5 requests/second
- No memory leaks over 1000 requests

### 10.4 Cost Monitoring Tests

**Track per request:**
- Intent classification cost: ~$0.0002
- Q&A cost: ~$0.01
- Planning cost: ~$0.02
- Execution cost: $0.02-0.10 (varies)
- Total cost reduction vs v1: > 60%

---

## 11. Monitoring & Observability

### 11.1 Metrics to Track

**Per Agent Type:**
```csharp
public class AgentMetrics
{
    public string AgentType { get; set; }
    public int RequestCount { get; set; }
    public double AverageResponseTime { get; set; }
    public double P95ResponseTime { get; set; }
    public double AverageCost { get; set; }
    public double SuccessRate { get; set; }
    public int TotalTokensUsed { get; set; }
}
```

**Per Process:**
```csharp
public class ProcessMetrics
{
    public string SessionId { get; set; }
    public IntentType Intent { get; set; }
    public int StepsExecuted { get; set; }
    public TimeSpan TotalDuration { get; set; }
    public Dictionary<string, double> StageTimings { get; set; } // Intent, Planning, Execution
    public double TotalCost { get; set; }
    public bool Success { get; set; }
}
```

### 11.2 Logging Strategy

**Structured Logging:**
```csharp
_logger.LogInformation(
    "Intent classified: {Intent} (Confidence: {Confidence})",
    result.Intent,
    result.Confidence);

_logger.LogInformation(
    "Plan created: {PlanId} with {StepCount} steps (Estimated: {Duration})",
    plan.PlanId,
    plan.Steps.Count,
    plan.EstimatedDuration);

_logger.LogInformation(
    "Step {StepId} completed: {Outcome} ({ExecutionTime}ms)",
    stepResult.StepId,
    stepResult.Outcome,
    stepResult.ExecutionTime.TotalMilliseconds);
```

### 11.3 Dashboards

**Key Metrics Dashboard:**
- Request volume by intent type
- Average cost per request type
- Success rate by agent
- P95 response times
- Token usage trends
- Cost savings vs v1

---

## 12. Migration Strategy

### 12.1 Phase 1: Infrastructure (Week 1)

**Tasks:**
1. Install Semantic Kernel Process packages
2. Implement ModelSettings configuration
3. Create KernelFactory
4. Add Google AI connector support
5. Update dependency injection

**Deliverables:**
- Multi-model configuration working
- Kernels created for each capability
- Unit tests passing

### 12.2 Phase 2: Agent Implementation (Week 2)

**Tasks:**
1. Implement IntentClassifierStep
2. Implement QAAgentStep with Gemini
3. Implement PlanningAgentStep with structured output
4. Implement ExecutionAgentStep with step tracking
5. Implement SimpleReplyStep

**Deliverables:**
- All agent steps functional
- Integration tests passing
- Accuracy benchmarks met

### 12.3 Phase 3: Process Framework (Week 3)

**Tasks:**
1. Build CodingOrchestrationProcess
2. Implement OrchestrationService
3. Add process state management
4. Implement event routing logic
5. Add error handling and retry

**Deliverables:**
- End-to-end process working
- State persistence functional
- Process visualization available

### 12.4 Phase 4: API & Integration (Week 4)

**Tasks:**
1. Add v2 API endpoints
2. Update frontend to call v2
3. Add backward compatibility layer
4. Implement feature flags
5. Add monitoring and metrics

**Deliverables:**
- v2 API production-ready
- Metrics dashboard live
- Documentation updated

### 12.5 Phase 5: Testing & Optimization (Week 5)

**Tasks:**
1. Run performance benchmarks
2. Optimize prompts for accuracy
3. Tune confidence thresholds
4. Load testing
5. Cost validation

**Deliverables:**
- Performance targets met
- Cost reduction validated (>60%)
- Production readiness confirmed

---

## 13. Risks & Mitigations

### 13.1 Technical Risks

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| Gemini API instability | High | Medium | Implement fallback to GPT-4o for Q&A |
| Intent classification accuracy < 95% | Medium | Low | Collect training data, fine-tune prompts |
| Process framework bugs | High | Low | Comprehensive testing, staged rollout |
| Increased latency | Medium | Medium | Optimize prompts, parallel execution |
| Cost exceeds estimates | High | Low | Monitor per-request, implement budgets |

### 13.2 Migration Risks

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| Breaking changes to existing API | High | Low | Maintain v1 compatibility, gradual migration |
| User confusion with new flow | Medium | Medium | Clear documentation, UI updates |
| Data migration issues | Medium | Low | Test with production data snapshots |

### 13.3 Operational Risks

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| Multiple model API keys required | Low | High | Secure key management, rotation policy |
| Increased complexity | Medium | High | Comprehensive documentation, training |
| Debugging difficulty | Medium | Medium | Enhanced logging, process visualization |

---

## 14. Success Criteria

### 14.1 Functional Requirements

- ✅ Intent classification accuracy > 95%
- ✅ Q&A responses include file/line references
- ✅ Plans validated before execution
- ✅ Step-by-step execution with progress tracking
- ✅ Error handling with retry logic
- ✅ Backward compatibility maintained

### 14.2 Performance Requirements

- ✅ Intent classification < 500ms (p95)
- ✅ Q&A response < 10s (p95)
- ✅ Planning < 15s (p95)
- ✅ Step execution < 30s per step (p95)
- ✅ Support 10 concurrent sessions

### 14.3 Cost Requirements

- ✅ Average cost reduction > 60% vs v1
- ✅ Intent classification cost < $0.001 per request
- ✅ Total cost per complex task < $0.20

### 14.4 Quality Requirements

- ✅ Plan execution success rate > 90%
- ✅ Zero security regressions
- ✅ Code coverage > 80%
- ✅ All integration tests passing

---

## 15. Documentation Requirements

### 15.1 Technical Documentation

**Files to Create:**
- Architecture diagram (process flow)
- Agent specification documents
- API documentation (OpenAPI/Swagger)
- Configuration guide
- Troubleshooting guide

### 15.2 User Documentation

**Updates Required:**
- README.md with v2 architecture overview
- User guide for new endpoints
- Migration guide from v1 to v2
- Examples of each agent type

### 15.3 Developer Documentation

**Files to Create:**
- Contributing guide for new agents
- Testing strategy document
- Prompt engineering guidelines
- Model selection rationale

---

## 16. Dependencies

### 16.1 NuGet Packages

**New Packages Required:**
```xml
<!-- Semantic Kernel Process Framework -->
<PackageReference Include="Microsoft.SemanticKernel.Process" Version="1.66.0" />

<!-- Google AI Connector -->
<PackageReference Include="Microsoft.SemanticKernel.Connectors.Google" Version="1.66.0" />

<!-- JSON Schema for structured output -->
<PackageReference Include="NJsonSchema" Version="11.0.0" />

<!-- Additional telemetry -->
<PackageReference Include="Microsoft.ApplicationInsights.AspNetCore" Version="2.22.0" />
```

### 16.2 External Services

- Azure OpenAI (existing)
- Google AI API (new - requires API key)
- Optional: OpenAI API (fallback)

### 16.3 Infrastructure

- No additional infrastructure required
- Same SQLite session store
- Same workspace structure
- Same security constraints

---

## 17. Acceptance Criteria

### 17.1 Demo Scenarios

**Scenario 1: Question Flow**
```
Input: "How does the SecurityService prevent path traversal attacks?"

Expected:
1. Intent classifier identifies as QUESTION (confidence > 0.9)
2. Routed to QAAgent (Gemini)
3. Response includes:
   - Explanation of ValidateFilePathAsync method
   - Reference to SecurityService.cs:45-67
   - Code snippet showing validation logic
4. Total time < 10s
5. Cost < $0.02
```

**Scenario 2: Simple Task Flow**
```
Input: "Add a comment to the ValidateFilePathAsync method explaining what it does"

Expected:
1. Intent classifier identifies as TASK (confidence > 0.85)
2. Routed to PlanningAgent
3. Plan created with 3 steps:
   - Read SecurityService.cs
   - Add comment
   - Verify syntax
4. ExecutionAgent executes all steps successfully
5. File modified with appropriate comment
6. Total time < 2 minutes
7. Cost < $0.10
```

**Scenario 3: Complex Task Flow**
```
Input: "Add unit tests for all methods in SecurityService"

Expected:
1. Intent classifier identifies as TASK (confidence > 0.9)
2. Plan created with 8-12 steps
3. All steps execute successfully
4. Test file created with 15+ test methods
5. Tests pass when executed
6. Total time < 5 minutes
7. Cost < $0.20
```

### 17.2 Performance Benchmarks

**Run 100 requests across different intents:**
- 40 questions
- 40 simple tasks
- 20 complex tasks

**Measure:**
- Average response time by type
- P95 response time by type
- Success rate by type
- Average cost by type
- Total cost reduction vs v1 baseline

**Target:**
- Questions: avg 5s, p95 10s, success 95%+
- Simple tasks: avg 1.5min, p95 2.5min, success 90%+
- Complex tasks: avg 4min, p95 6min, success 85%+
- Cost reduction: >60% overall

---

## 18. Next Steps

### 18.1 Immediate Actions (Before Implementation)

1. **Review & Approval**
   - Review this requirements document with stakeholders
   - Get approval on architecture approach
   - Confirm budget for Google AI API

2. **Environment Setup**
   - Obtain Google AI API key
   - Test Gemini API connectivity
   - Verify Semantic Kernel Process package availability

3. **Proof of Concept**
   - Build minimal process with 2 steps
   - Validate event routing works
   - Test state persistence

### 18.2 Implementation Kickoff

1. Create feature branch: `feature/multi-agent-orchestration`
2. Set up project board with tasks from migration strategy
3. Begin Phase 1: Infrastructure implementation
4. Daily standups to track progress

### 18.3 Questions to Resolve

- [ ] Confirm Google AI API budget/limits
- [ ] Decide on fallback strategy if Gemini unavailable
- [ ] Choose process visualization tool
- [ ] Determine metrics storage (Application Insights vs custom)
- [ ] Decide on v1 deprecation timeline

---

## Appendix A: References

- [Semantic Kernel Process Framework Docs](https://learn.microsoft.com/en-us/semantic-kernel/concepts/process)
- [Google AI Gemini API](https://ai.google.dev/docs)
- [Azure OpenAI Service](https://learn.microsoft.com/en-us/azure/ai-services/openai/)
- [Semantic Kernel Connectors](https://learn.microsoft.com/en-us/semantic-kernel/concepts/connectors)

## Appendix B: Glossary

- **Process** - A workflow definition containing multiple steps and event routing
- **Step** - An individual processing unit within a process (maps to an agent)
- **Event** - A trigger that causes transition between steps
- **Kernel** - Semantic Kernel instance configured with model + plugins
- **Plugin** - Collection of kernel functions (tools) available to agents
- **Intent** - Classified category of user input (Question, Task, Greeting, Unclear)
- **Plan** - Structured list of steps to execute for a task
- **Step Result** - Outcome of executing a single plan step

---

**Document Owner:** Development Team
**Last Updated:** 2025-10-20
**Next Review:** After Phase 1 completion
