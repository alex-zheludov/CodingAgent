# Functional Requirements Document: Autonomous Coding Agent

**Version:** 1.0  
**Date:** October 16, 2025  
**Document Type:** Functional Requirements Only

---

## 1. Overview

### 1.1 Purpose
The Autonomous Coding Agent is a containerized service that autonomously performs software development tasks on one or more Git repositories based on natural language instructions.

### 1.2 Scope
This document defines **what** the agent must do. Implementation details, technology choices, and architecture are outside the scope of this document.

---

## 2. Container Lifecycle Requirements

### 2.1 Startup Requirements

**REQ-START-001:** The agent MUST initialize from environment variables only
- No configuration files
- All settings passed as environment variables
- Must validate required environment variables on startup

**REQ-START-002:** The agent MUST support SSH-based Git authentication
- Accept SSH private key via environment variable (base64 encoded OR file path)
- Configure Git to use provided SSH key
- Establish SSH connectivity before proceeding

**REQ-START-003:** The agent MUST clone one or more Git repositories on startup
- Accept repository configuration as JSON in environment variable
- Support multiple repositories in single agent instance
- Each repository configuration must include: name, SSH URL, branch
- Clone all repositories before accepting work instructions
- Report clone status (success/failure) for each repository

**REQ-START-004:** The agent MUST scan workspace and generate initial context
- Analyze directory structure of all repositories
- Count files by type/extension
- Identify key files (README, project files, configuration)
- Store initial context for use during task execution

**REQ-START-005:** The agent MUST persist session state locally
- Store conversation history
- Store agent thinking/reasoning log
- Store status updates
- Survive container restart (state persists between restarts)

**REQ-START-006:** The agent MUST be ready to accept requests within 2 minutes of container start
- Complete all initialization steps
- Report "ready" status
- Expose health check endpoint

### 2.2 Shutdown Requirements

**REQ-STOP-001:** The agent MUST support graceful shutdown
- Save all pending state
- Complete in-progress operations where possible
- Respond to stop signals appropriately

**REQ-STOP-002:** The agent MUST be able to pause and resume
- Save checkpoint of current state
- Resume from checkpoint after container restart
- Maintain conversation context across restarts

---

## 3. Communication Requirements

### 3.1 HTTP API Requirements

**REQ-API-001:** The agent MUST expose HTTP endpoint to receive initial task instruction
- Accept natural language instruction
- Return acknowledgment immediately
- Process instruction asynchronously

**REQ-API-002:** The agent MUST expose HTTP endpoint to receive follow-up messages
- Accept additional instructions during execution
- Support clarification responses from user
- Maintain conversation context

**REQ-API-003:** The agent MUST expose HTTP endpoint to query current status
- Return current agent state (working, idle, complete, needs_clarification)
- Return current activity description
- Return thinking log (recent thoughts/reasoning)
- Return progress information
- Return repository status (modified files, branches, etc.)

**REQ-API-004:** The agent MUST expose HTTP endpoint to retrieve conversation history
- Return all messages in chronological order
- Include user messages, agent responses, and tool calls
- Support pagination for long conversations

**REQ-API-005:** The agent MUST expose health check endpoint
- Return overall health status
- Return status of critical subsystems (workspace, git, session store)
- Return uptime and last activity timestamp

### 3.2 SignalR Requirements (Future Orchestrator Integration)

**REQ-SIGNALR-001:** The agent MUST support SignalR client connection to orchestrator
- Connect to orchestrator hub on startup
- Maintain persistent connection
- Reconnect automatically on connection loss
- Accept orchestrator hub URL from environment variable

**REQ-SIGNALR-002:** The agent MUST send real-time updates to orchestrator via SignalR
- Send status changes
- Send thinking/reasoning updates
- Send tool execution notifications
- Send completion or clarification requests

**REQ-SIGNALR-003:** The agent MUST receive messages from orchestrator via SignalR
- Receive user instructions
- Receive pause/resume commands
- Receive termination commands

**REQ-SIGNALR-004:** The agent MUST use shared logic for HTTP and SignalR messages
- Same processing logic for messages from HTTP or SignalR
- Consistent behavior regardless of communication method

---

## 4. Autonomous Operation Requirements

### 4.1 Task Execution Requirements

**REQ-EXEC-001:** The agent MUST process natural language instructions autonomously
- Understand task requirements from natural language
- Break down complex tasks into subtasks
- Execute subtasks in logical order
- Work without human intervention until complete or blocked

**REQ-EXEC-002:** The agent MUST discover relevant code autonomously
- Search for relevant files across all repositories
- Navigate directory structures intelligently
- Read only necessary files (avoid reading entire codebase)
- Understand relationships between files and repositories

**REQ-EXEC-003:** The agent MUST be aware of multi-repository context
- Understand which repository contains which functionality
- Coordinate changes across multiple repositories when needed
- Reference cross-repository dependencies appropriately

**REQ-EXEC-004:** The agent MUST explain its reasoning
- Document thinking process in thinking log
- Explain why specific files are being read/modified
- Explain why specific tools are being used
- Provide progress updates during long-running operations

### 4.2 Code Modification Requirements

**REQ-MODIFY-001:** The agent MUST read file contents
- Support reading any text file in workspace
- Handle files from any repository
- Return file contents with context (path, size)

**REQ-MODIFY-002:** The agent MUST write file contents
- Support writing any text file in workspace
- Create directories as needed
- Overwrite existing files
- Report write success/failure

**REQ-MODIFY-003:** The agent MUST search for files and code
- Search by filename pattern (wildcards)
- Search by file contents (text/regex)
- Search across all repositories
- Search within specific repository
- Return ranked/relevant results

**REQ-MODIFY-004:** The agent MUST understand codebase structure
- List directory contents
- Generate directory tree visualization
- Identify file types and counts
- Locate key files (configuration, entry points)

**REQ-MODIFY-005:** The agent MUST analyze code context
- Extract dependencies/imports from files
- Identify classes, interfaces, methods
- Understand file relationships

### 4.3 Git Operation Requirements

**REQ-GIT-001:** The agent MUST check repository status
- Report modified files
- Report staged files
- Report untracked files
- Report current branch
- Support per-repository or all repositories

**REQ-GIT-002:** The agent MUST show file differences
- Display diff for specific file
- Display diff for all changes
- Support unified diff format

**REQ-GIT-003:** The agent MUST stage changes for commit
- Stage specific files
- Stage multiple files
- Support staging in any repository

**REQ-GIT-004:** The agent MUST commit changes
- Create commits with descriptive messages
- Support per-repository commits
- Include author information (configurable)

**REQ-GIT-005:** The agent MUST create and manage branches
- Create new branch from current branch
- Checkout/switch to branch
- Report current branch
- Use configurable branch name prefix (e.g., "agent/feature-name")

**REQ-GIT-006:** The agent MUST push changes to remote
- Push commits to remote repository
- Support configurable remote name (default: origin)
- Use SSH authentication
- Report push success/failure

**REQ-GIT-007:** The agent MUST view commit history
- Display recent commits
- Show commit SHA, message, author, date
- Support configurable commit count

### 4.4 Build and Test Requirements

**REQ-BUILD-001:** The agent MUST execute build commands
- Support .NET builds (dotnet build)
- Support Node.js builds (npm install, npm run build)
- Execute in correct repository directory
- Capture build output and errors
- Report build success/failure

**REQ-BUILD-002:** The agent MUST execute test commands
- Support .NET tests (dotnet test)
- Support Node.js tests (npm test)
- Execute in correct repository directory
- Capture test output and results
- Report test success/failure

**REQ-BUILD-003:** The agent MUST execute whitelisted commands
- Execute only explicitly allowed commands
- Block dangerous commands (rm -rf, format, etc.)
- Block command chaining (&&, ||, ;, |)
- Execute in correct working directory
- Apply configurable timeout
- Capture command output (stdout and stderr)
- Report exit code

### 4.5 Completion and Clarification Requirements

**REQ-COMPLETE-001:** The agent MUST determine when task is complete
- Detect completion indicators in own responses
- Verify all requested changes implemented
- Confirm tests passing (if applicable)
- Confirm changes committed (if auto-commit enabled)
- Confirm changes pushed (if auto-push enabled)
- Send explicit completion signal

**REQ-COMPLETE-002:** The agent MUST request clarification when needed
- Detect ambiguous requirements
- Identify missing information
- Formulate specific clarification questions
- Send clarification request signal
- Include context with clarification question

**REQ-COMPLETE-003:** The agent MUST respect execution time limits
- Track elapsed execution time
- Request clarification if execution exceeds configured limit (e.g., 30 minutes)
- Summarize progress when requesting time-based clarification

**REQ-COMPLETE-004:** The agent MUST signal its status clearly
- Report status: working, idle, complete, needs_clarification, error
- Include clear description of current activity
- Provide actionable information for each status

---

## 5. Security Requirements

### 5.1 File System Security

**REQ-SEC-001:** The agent MUST restrict file access to workspace only
- Prevent access to files outside workspace directory
- Prevent path traversal attacks (../)
- Prevent access to system files (/etc, /sys, /proc, etc.)
- Log all file access attempts

**REQ-SEC-002:** The agent MUST validate all file paths
- Normalize paths before access
- Verify paths are within allowed boundaries
- Reject suspicious patterns
- Reject binary file operations (.exe, .dll, .so, etc.)

**REQ-SEC-003:** The agent MUST enforce file size limits
- Reject reading files above configured size limit
- Reject writing files above configured size limit
- Provide clear error messages for size violations

### 5.2 Command Execution Security

**REQ-SEC-004:** The agent MUST whitelist allowed commands
- Accept only explicitly allowed commands
- Reject all non-whitelisted commands
- Support configurable command whitelist via environment variable

**REQ-SEC-005:** The agent MUST block dangerous command patterns
- Block destructive operations (rm -rf, del /f, format, etc.)
- Block network operations (curl, wget, nc, etc.)
- Block privilege escalation (sudo, chmod +x, etc.)
- Block command chaining and piping

**REQ-SEC-006:** The agent MUST enforce command timeouts
- Apply configurable timeout to all command executions
- Terminate commands that exceed timeout
- Report timeout violations

### 5.3 SSH Key Security

**REQ-SEC-007:** The agent MUST handle SSH keys securely
- Accept SSH key via secure environment variable
- Store key with proper permissions (600)
- Never log or expose SSH key contents
- Remove SSH key from memory after use (if applicable)

### 5.4 Audit and Logging

**REQ-SEC-008:** The agent MUST log all security-relevant events
- Log file access attempts (allowed and denied)
- Log command execution attempts (allowed and denied)
- Log git operations
- Log authentication attempts
- Include timestamps on all log entries

---

## 6. Configuration Requirements

### 6.1 Configuration Source

**REQ-CONFIG-001:** The agent MUST support .NET configuration system (IConfiguration)
- Read configuration from appsettings.json
- Support environment variable overrides
- Follow standard .NET configuration hierarchy:
  1. appsettings.json (base configuration)
  2. appsettings.{Environment}.json (environment-specific)
  3. Environment variables (highest priority, override all)

**REQ-CONFIG-002:** The agent MUST validate all required configuration values on startup
- Check presence of required values
- Validate format and types
- Fail fast with clear error messages if configuration is invalid

### 6.2 Required Configuration

**REQ-CONFIG-003:** Claude:ApiKey
- Claude AI API key for LLM access
- Required
- Type: String
- Can be set via environment variable: CLAUDE__APIKEY

**REQ-CONFIG-004:** Agent:SessionId
- Unique identifier for this agent session
- Required
- Type: String (GUID format)
- Can be set via environment variable: AGENT__SESSIONID

**REQ-CONFIG-005:** Agent:Repositories
- List of Git repositories to clone and work with
- Required (minimum 1 repository)
- Type: Array of objects
- Each repository must have: Name, Url, Branch

**REQ-CONFIG-006:** Agent:Workspace:Root
- Root directory where repositories are cloned
- Required
- Type: String (absolute path)
- Can be set via environment variable: AGENT__WORKSPACE__ROOT

**REQ-CONFIG-007:** Git:SshKeyPath OR Git:SshKeyBase64
- SSH private key for Git authentication
- Required (one of the two)
- Type: String (file path OR base64 encoded key)
- Can be set via environment variables: GIT__SSHKEYPATH or GIT__SSHKEYBASE64

### 6.3 Optional Configuration

**REQ-CONFIG-008:** Orchestrator:HubUrl
- SignalR hub URL for orchestrator communication
- Optional (null disables SignalR)
- Type: String (URL)
- Default: null

**REQ-CONFIG-009:** Orchestrator:ApiKey
- API key for authenticating with orchestrator
- Optional
- Type: String
- Default: null

**REQ-CONFIG-010:** Claude:Model
- Claude model identifier to use
- Optional
- Type: String
- Default: "claude-3-5-sonnet-20241022"

**REQ-CONFIG-011:** Claude:MaxTokens
- Maximum tokens for Claude responses
- Optional
- Type: Integer
- Default: 4096

**REQ-CONFIG-012:** Claude:Temperature
- Temperature setting for Claude
- Optional
- Type: Decimal (0.0 to 1.0)
- Default: 0.3

**REQ-CONFIG-013:** Agent:MaxExecutionMinutes
- Maximum execution time before requesting clarification
- Optional
- Type: Integer
- Default: 30

**REQ-CONFIG-014:** Agent:AutoCommit
- Automatically commit successful changes
- Optional
- Type: Boolean
- Default: true

**REQ-CONFIG-015:** Agent:AutoPush
- Automatically push commits to remote
- Optional
- Type: Boolean
- Default: false

**REQ-CONFIG-016:** Git:TargetBranchPrefix
- Prefix for agent-created branches
- Optional
- Type: String
- Default: "agent/"

**REQ-CONFIG-017:** Git:CommitAuthorName
- Name to use for git commits
- Optional
- Type: String
- Default: "Code Agent"

**REQ-CONFIG-018:** Git:CommitAuthorEmail
- Email to use for git commits
- Optional
- Type: String
- Default: "agent@codeagent.local"

**REQ-CONFIG-019:** Security:AllowedCommands
- List of allowed shell commands
- Optional
- Type: Array of strings
- Default: ["dotnet build", "dotnet test", "dotnet restore", "npm install", "npm test", "npm run"]

**REQ-CONFIG-020:** Security:FileSizeLimitMB
- Maximum file size for read/write operations in megabytes
- Optional
- Type: Integer
- Default: 10

**REQ-CONFIG-021:** Logging:LogLevel:Default
- Default logging verbosity level
- Optional
- Type: String (Debug, Information, Warning, Error, Critical)
- Default: "Information"

---

## 7. Data Persistence Requirements

### 7.1 Session State Persistence

**REQ-PERSIST-001:** The agent MUST persist conversation history
- Store all user messages
- Store all agent responses
- Store tool call information
- Persist across container restarts

**REQ-PERSIST-002:** The agent MUST persist thinking log
- Store agent reasoning and thought process
- Include timestamps
- Persist across container restarts

**REQ-PERSIST-003:** The agent MUST persist status updates
- Store status changes over time
- Include activity descriptions
- Include timestamps
- Persist across container restarts

**REQ-PERSIST-004:** The agent MUST persist session metadata
- Store session ID
- Store initialization timestamp
- Store repository information
- Store configuration parameters
- Persist across container restarts

### 7.2 Storage Location

**REQ-PERSIST-005:** The agent MUST store session state in workspace
- Use local storage within workspace directory
- Store in hidden directory (e.g., .session/)
- Support configurable storage location

---

## 8. Error Handling Requirements

### 8.1 Startup Errors

**REQ-ERROR-001:** The agent MUST validate environment variables
- Check all required variables present
- Validate variable formats and values
- Report specific missing/invalid variables
- Fail fast with clear error messages

**REQ-ERROR-002:** The agent MUST handle repository clone failures
- Report which repository failed to clone
- Include error details
- Support partial success (some repos cloned, others failed)

**REQ-ERROR-003:** The agent MUST handle SSH authentication failures
- Report SSH connectivity issues
- Provide guidance for resolution
- Log authentication attempts

### 8.2 Execution Errors

**REQ-ERROR-004:** The agent MUST handle file operation errors
- Report file not found errors
- Report permission errors
- Report disk space errors
- Continue execution when possible

**REQ-ERROR-005:** The agent MUST handle git operation errors
- Report merge conflicts
- Report authentication failures
- Report network errors
- Provide recovery suggestions

**REQ-ERROR-006:** The agent MUST handle command execution errors
- Capture command errors (stderr)
- Report non-zero exit codes
- Include error output in response
- Continue execution when possible

**REQ-ERROR-007:** The agent MUST handle LLM API errors
- Handle rate limiting gracefully
- Handle timeouts
- Handle network errors
- Retry with exponential backoff when appropriate

### 8.3 Error Recovery

**REQ-ERROR-008:** The agent MUST attempt error recovery when possible
- Retry transient failures
- Suggest corrective actions
- Ask for user guidance when blocked
- Maintain state during recovery attempts

**REQ-ERROR-009:** The agent MUST report unrecoverable errors clearly
- Explain what failed
- Explain why it failed
- Suggest manual intervention if needed
- Preserve all state for debugging

---

## 9. Performance Requirements

**REQ-PERF-001:** The agent MUST complete initialization within 2 minutes
- Clone repositories
- Scan workspace
- Initialize session
- Ready to accept requests

**REQ-PERF-002:** The agent MUST respond to status queries within 500ms
- Return current status
- Return recent activity
- Return thinking log

**REQ-PERF-003:** The agent MUST handle file operations efficiently
- Avoid reading unnecessary files
- Cache frequently accessed information
- Use streaming for large files when possible

**REQ-PERF-004:** The agent MUST manage token usage efficiently
- Include only relevant context in LLM requests
- Use initial context scan to reduce redundant queries
- Avoid including entire files in prompts when possible

---

## 10. Quality Requirements

### 10.1 Code Modification Quality

**REQ-QUALITY-001:** The agent MUST make precise, targeted code changes
- Modify only necessary lines
- Preserve existing code style and formatting
- Follow language-specific conventions
- Maintain code readability

**REQ-QUALITY-002:** The agent MUST verify changes before committing
- Review diff before commit
- Ensure changes match intent
- Check for unintended modifications

**REQ-QUALITY-003:** The agent MUST write descriptive commit messages
- Summarize what changed
- Explain why changes were made
- Reference related changes in other repositories when applicable
- Follow conventional commit format when possible

### 10.2 Communication Quality

**REQ-QUALITY-004:** The agent MUST communicate clearly
- Use natural, professional language
- Avoid technical jargon when unnecessary
- Provide sufficient context
- Be concise but informative

**REQ-QUALITY-005:** The agent MUST provide helpful status updates
- Report current activity clearly
- Explain what's happening and why
- Estimate remaining work when possible
- Signal progress regularly during long operations

---

## 11. Success Criteria

### 11.1 Functional Success Criteria

**SUCCESS-001:** Container starts successfully and reports ready status within 2 minutes

**SUCCESS-002:** Agent successfully clones all configured repositories with valid SSH credentials

**SUCCESS-003:** Agent correctly processes natural language instructions and performs requested code changes

**SUCCESS-004:** Agent autonomously discovers relevant files across multiple repositories

**SUCCESS-005:** Agent successfully executes git operations (status, diff, commit, push)

**SUCCESS-006:** Agent successfully executes build and test commands

**SUCCESS-007:** Agent signals completion when task is done

**SUCCESS-008:** Agent requests clarification when requirements are ambiguous

**SUCCESS-009:** Agent respects all security constraints (file access, command execution)

**SUCCESS-010:** Agent maintains conversation context across container restarts

**SUCCESS-011:** HTTP API endpoints respond correctly and return expected data

**SUCCESS-012:** SignalR client connects successfully to orchestrator (when configured)

### 11.2 Non-Functional Success Criteria

**SUCCESS-013:** All security validations pass (path traversal, command injection, etc.)

**SUCCESS-014:** No unauthorized file system access occurs

**SUCCESS-015:** No disallowed commands execute

**SUCCESS-016:** Session state persists correctly across restarts

**SUCCESS-017:** Performance requirements met (startup time, API response time)

---

## 12. Out of Scope

The following items are explicitly **out of scope** for the Coding Agent:

**OUT-001:** Container orchestration and lifecycle management
- Creating/destroying containers
- Scaling containers
- Load balancing
- Resource allocation
- *Responsibility: Orchestrator*

**OUT-002:** User interface and user experience
- Web UI
- CLI tools
- IDE integrations
- *Responsibility: Orchestrator + UI*

**OUT-003:** User authentication and authorization
- Login/logout
- User management
- Permission systems
- *Responsibility: Orchestrator*

**OUT-004:** Multi-tenant isolation
- Separate workspaces per user
- Resource quotas per user
- Cost tracking per user
- *Responsibility: Orchestrator*

**OUT-005:** Long-term data persistence beyond session
- Historical analytics
- Cross-session insights
- Audit trails
- *Responsibility: Orchestrator*

**OUT-006:** Repository management
- Adding/removing repositories
- Changing repository configuration
- Repository access control
- *Responsibility: Orchestrator*

**OUT-007:** External integrations
- GitHub/GitLab API integration
- Jira/issue tracker integration
- CI/CD pipeline integration
- Notification systems (email, Slack, etc.)
- *Responsibility: Orchestrator*

---

## 13. Assumptions and Dependencies

### 13.1 Assumptions

**ASSUME-001:** Docker or compatible container runtime is available

**ASSUME-002:** Orchestrator exists and will manage agent container lifecycle (future)

**ASSUME-003:** SSH keys for Git repositories are valid and have appropriate permissions

**ASSUME-004:** Git repositories are accessible from agent container network

**ASSUME-005:** Claude API is accessible from agent container network

**ASSUME-006:** Workspace directory has sufficient disk space for repositories and session data

**ASSUME-007:** Agent has network access to required services (Git hosts, Claude API)

### 13.2 External Dependencies

**DEP-001:** Claude AI API
- Availability: Must be accessible
- Performance: Response time affects agent performance
- Cost: API usage incurs costs

**DEP-002:** Git remote repositories
- Availability: Must be accessible for clone/push operations
- Authentication: SSH keys must be valid

**DEP-003:** Container runtime
- Docker or compatible container runtime
- Sufficient resources (CPU, memory, disk)

**DEP-004:** Orchestrator (future)
- SignalR hub endpoint
- Must accept agent connections
- Must handle agent messages

---

## 14. Glossary

**Agent:** The autonomous coding service described in this document

**Orchestrator:** The parent service that manages agent containers (out of scope for this document)

**Session:** A single work session for the agent, corresponding to one container instance

**Workspace:** The directory where repositories are cloned and work is performed

**Repository:** A Git repository cloned and managed by the agent

**Tool:** A capability the agent can use (e.g., read file, execute command, git commit)

**Thinking Log:** Record of agent's reasoning and decision-making process

**Clarification Request:** When agent asks user for more information to proceed

**Completion Signal:** Notification that agent has finished requested work

---

## Appendix A: System Architecture

### Complete System Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────────────┐
│                                 USER                                     │
│                          (Browser / CLI / IDE)                           │
└────────────────────────────────┬────────────────────────────────────────┘
                                 │
                                 │ HTTPS
                                 │
┌────────────────────────────────▼────────────────────────────────────────┐
│                            ORCHESTRATOR                                  │
│  ┌───────────────────────────────────────────────────────────────────┐  │
│  │                        Blazor Web UI                              │  │
│  │  • Session Management UI                                          │  │
│  │  • Chat Interface (real-time)                                     │  │
│  │  • Repository Configuration                                       │  │
│  │  • Agent Status Dashboard                                         │  │
│  └────────────────────────────┬──────────────────────────────────────┘  │
│                                │                                         │
│  ┌────────────────────────────▼──────────────────────────────────────┐  │
│  │                   ASP.NET Core Backend                            │  │
│  │  • Session CRUD API                                               │  │
│  │  • Container Lifecycle Management                                 │  │
│  │  • SignalR Hub (Agent ↔ Orchestrator ↔ User)                     │  │
│  │  • Message Routing                                                │  │
│  └────────────────────────────┬──────────────────────────────────────┘  │
│                                │                                         │
│  ┌────────────────────────────▼──────────────────────────────────────┐  │
│  │                      PostgreSQL Database                          │  │
│  │  • Session metadata                                               │  │
│  │  • User accounts (future)                                         │  │
│  │  • Conversation history archives                                  │  │
│  │  • Audit logs                                                     │  │
│  └───────────────────────────────────────────────────────────────────┘  │
│                                                                          │
│  Container Management:                                                   │
│  ┌────────────────────────────────────────────────────────────────┐    │
│  │  Docker API Integration                                         │    │
│  │  • Create containers                                            │    │
│  │  • Start/Stop containers                                        │    │
│  │  • Monitor container health                                     │    │
│  │  • Cleanup terminated containers                                │    │
│  └────────────────────────────────────────────────────────────────┘    │
└────────────────────────────────┬────────────────────────────────────────┘
                                 │
                                 │ Docker Network
                                 │ SignalR (WebSocket)
                                 │
          ┌──────────────────────┼──────────────────────┐
          │                      │                      │
          │                      │                      │
┌─────────▼────────────┐  ┌─────▼──────────┐  ┌──────▼────────────┐
│  AGENT CONTAINER 1   │  │  AGENT         │  │  AGENT            │
│  (Session A)         │  │  CONTAINER 2   │  │  CONTAINER 3      │
│                      │  │  (Session B)   │  │  (Session C)      │
│ ┌──────────────────┐ │  │                │  │                   │
│ │ Minimal API      │ │  │  (Same         │  │  (Same            │
│ │ + SignalR Client │ │  │   structure    │  │   structure       │
│ └────────┬─────────┘ │  │   as           │  │   as              │
│          │           │  │   Container 1) │  │   Container 1)    │
│ ┌────────▼─────────┐ │  │                │  │                   │
│ │ Message Handler  │ │  └────────────────┘  └───────────────────┘
│ │ (Shared Logic)   │ │
│ └────────┬─────────┘ │
│          │           │
│ ┌────────▼─────────┐ │
│ │ Semantic Kernel  │ │
│ │ + Claude AI      │ │
│ └────────┬─────────┘ │
│          │           │
│ ┌────────▼─────────┐ │
│ │ Plugins:         │ │
│ │ • FileOps        │ │
│ │ • CodeNav        │ │
│ │ • Git            │ │
│ │ • Commands       │ │
│ └────────┬─────────┘ │
│          │           │
│ ┌────────▼─────────┐ │
│ │ Services:        │ │
│ │ • Security       │ │
│ │ • Workspace Mgr  │ │
│ │ • Session Store  │ │
│ └────────┬─────────┘ │
│          │           │
│ ┌────────▼─────────┐ │
│ │ SQLite Session   │ │
│ │ Database         │ │
│ └──────────────────┘ │
│                      │
│ ┌──────────────────┐ │
│ │ WORKSPACE        │ │
│ │ /workspace/      │ │
│ │  ├── repo1/      │ │
│ │  ├── repo2/      │ │
│ │  └── .session/   │ │
│ └──────────────────┘ │
└──────────────────────┘

                │
                │ HTTPS
                │
┌───────────────▼──────────────┐
│      Claude AI API           │
│   (Anthropic)                │
└──────────────────────────────┘

                │
                │ SSH/HTTPS
                │
┌───────────────▼──────────────┐
│   Git Remote Repositories    │
│   • GitHub                   │
│   • GitLab                   │
│   • Azure DevOps             │
│   • Self-hosted Git          │
└──────────────────────────────┘
```

### Architecture Flow Description

**User Interaction Flow:**
1. User accesses Orchestrator web UI (Blazor)
2. User creates new session, provides repositories and instructions
3. Orchestrator creates Docker container for Agent
4. Orchestrator establishes SignalR connection with Agent
5. Orchestrator sends initial instruction to Agent via SignalR
6. Agent processes instruction autonomously
7. Agent sends real-time updates back to Orchestrator via SignalR
8. Orchestrator forwards updates to User via SignalR (browser WebSocket)
9. User sees live progress in UI
10. When Agent needs clarification, user responds via UI
11. Orchestrator forwards clarification to Agent
12. Agent completes work and signals completion
13. Orchestrator stops/destroys Agent container after timeout

**Agent Internal Flow:**
1. Container starts, reads configuration (appsettings.json + env vars)
2. Sets up SSH keys for Git authentication
3. Clones all configured repositories
4. Scans workspace and generates initial context
5. Initializes Semantic Kernel with Claude AI
6. Registers all plugins (FileOps, CodeNav, Git, Commands)
7. Connects to Orchestrator SignalR hub (if configured)
8. Exposes HTTP API endpoints
9. Reports "ready" status
10. Receives instruction (HTTP or SignalR)
11. Processes via MessageHandler (shared logic)
12. Semantic Kernel invokes Claude AI with auto function calling
13. Claude AI autonomously uses plugins to:
    - Search for relevant files
    - Read code
    - Analyze structure
    - Make modifications
    - Execute builds/tests
    - Commit to Git
    - Push to remote
14. Agent reports progress continuously
15. Agent signals completion or requests clarification
16. Maintains state in SQLite for restart resilience

**Data Persistence:**
- **Orchestrator PostgreSQL:** Long-term session metadata, user data, archives
- **Agent SQLite:** Session-specific conversation, thinking log, status (ephemeral per container)
- **Workspace Files:** Modified code, git repositories (can be ephemeral or mounted volume)

---

## Appendix B: Workspace Structure

### Expected Workspace Directory Structure

After initialization, the agent's workspace should look like this:

```
/workspace/                                    # Root workspace directory
│
├── .session/                                  # Hidden session directory
│   ├── session.db                            # SQLite database
│   ├── metadata.json                         # Session initialization metadata
│   └── logs/                                 # Structured logs (optional)
│       └── agent-{date}.log
│
├── main-service/                              # Repository 1 (cloned)
│   ├── .git/                                 # Git repository data
│   ├── src/
│   │   ├── Controllers/
│   │   │   ├── UserController.cs
│   │   │   └── AdminController.cs
│   │   ├── Services/
│   │   │   ├── UserService.cs
│   │   │   └── AuthService.cs
│   │   ├── Models/
│   │   └── Program.cs
│   ├── tests/
│   │   └── UserServiceTests.cs
│   ├── MainService.csproj
│   ├── appsettings.json
│   └── README.md
│
├── shared-library/                            # Repository 2 (cloned)
│   ├── .git/
│   ├── src/
│   │   ├── Common/
│   │   │   ├── Validators/
│   │   │   ├── Extensions/
│   │   │   └── Utilities/
│   │   └── SharedLib.csproj
│   ├── tests/
│   └── README.md
│
└── api-contracts/                             # Repository 3 (cloned)
    ├── .git/
    ├── contracts/
    │   ├── user.proto
    │   ├── admin.proto
    │   └── common.proto
    ├── openapi/
    │   └── api-spec.yaml
    └── README.md
```

### Workspace Structure Requirements

**WORKSPACE-001:** Each repository must be cloned into its own subdirectory
- Directory name matches repository "name" from configuration
- Full Git repository structure preserved (.git directory)

**WORKSPACE-002:** Session metadata must be stored in hidden .session directory
- SQLite database for conversation history
- JSON metadata file for initialization context
- Log files (optional)

**WORKSPACE-003:** No files should exist outside of cloned repositories and .session
- Clean separation between repositories
- No temp files in workspace root

**WORKSPACE-004:** After successful execution, workspace may contain:
- New branches in repositories
- Modified source files
- New commits in Git history
- Build artifacts (bin/, obj/, node_modules/) - may be cleaned up

---

## Appendix C: Complete Configuration Example

### appsettings.json (Base Configuration)

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "System": "Warning"
    }
  },
  "AllowedHosts": "*",
  
  "Claude": {
    "ApiKey": "",
    "Model": "claude-3-5-sonnet-20241022",
    "MaxTokens": 4096,
    "Temperature": 0.3
  },
  
  "Agent": {
    "SessionId": "",
    "MaxExecutionMinutes": 30,
    "AutoCommit": true,
    "AutoPush": false,
    "Repositories": [],
    "Workspace": {
      "Root": "/workspace"
    }
  },
  
  "Git": {
    "SshKeyPath": "",
    "SshKeyBase64": "",
    "TargetBranchPrefix": "agent/",
    "CommitAuthorName": "Code Agent",
    "CommitAuthorEmail": "agent@codeagent.local"
  },
  
  "Orchestrator": {
    "HubUrl": null,
    "ApiKey": null
  },
  
  "Security": {
    "AllowedCommands": [
      "dotnet build",
      "dotnet test",
      "dotnet run",
      "dotnet restore",
      "dotnet clean",
      "npm install",
      "npm test",
      "npm run",
      "git status",
      "git log",
      "git diff"
    ],
    "FileSizeLimitMB": 10
  }
}
```

### appsettings.Development.json

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.AspNetCore": "Information"
    }
  },
  
  "Agent": {
    "SessionId": "dev-session-{guid}",
    "AutoPush": false,
    "Workspace": {
      "Root": "./workspace"
    }
  },
  
  "Git": {
    "SshKeyPath": "/Users/{username}/.ssh/id_rsa"
  }
}
```

### appsettings.Production.json

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  
  "Agent": {
    "Workspace": {
      "Root": "/workspace"
    }
  }
}
```

### Complete Configuration with All Values (Example for Testing)

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.AspNetCore": "Information",
      "System": "Warning"
    }
  },
  "AllowedHosts": "*",
  
  "Claude": {
    "ApiKey": "sk-ant-api03-...",
    "Model": "claude-3-5-sonnet-20241022",
    "MaxTokens": 4096,
    "Temperature": 0.3
  },
  
  "Agent": {
    "SessionId": "550e8400-e29b-41d4-a716-446655440000",
    "MaxExecutionMinutes": 30,
    "AutoCommit": true,
    "AutoPush": false,
    "Repositories": [
      {
        "Name": "main-service",
        "Url": "git@github.com:myorg/main-service.git",
        "Branch": "develop"
      },
      {
        "Name": "shared-library",
        "Url": "git@github.com:myorg/shared-lib.git",
        "Branch": "main"
      },
      {
        "Name": "api-contracts",
        "Url": "git@github.com:myorg/api-contracts.git",
        "Branch": "main"
      }
    ],
    "Workspace": {
      "Root": "/workspace"
    }
  },
  
  "Git": {
    "SshKeyPath": "/secrets/id_rsa",
    "SshKeyBase64": null,
    "TargetBranchPrefix": "agent/",
    "CommitAuthorName": "Code Agent",
    "CommitAuthorEmail": "agent@codeagent.local"
  },
  
  "Orchestrator": {
    "HubUrl": "https://orchestrator.example.com/hub",
    "ApiKey": "orch-key-abc123"
  },
  
  "Security": {
    "AllowedCommands": [
      "dotnet build",
      "dotnet test",
      "dotnet run",
      "dotnet restore",
      "dotnet clean",
      "npm install",
      "npm test",
      "npm run build",
      "npm run lint",
      "git status",
      "git log",
      "git diff"
    ],
    "FileSizeLimitMB": 10
  }
}
```

### Environment Variable Overrides

Any configuration value can be overridden using environment variables with the following pattern:

```bash
# Section:SubSection:Key format with double underscores
export Claude__ApiKey="sk-ant-api03-..."
export Agent__SessionId="550e8400-e29b-41d4-a716-446655440000"
export Agent__Workspace__Root="/workspace"
export Git__SshKeyPath="/secrets/id_rsa"
export Orchestrator__HubUrl="https://orchestrator/hub"

# Arrays use indexed notation
export Agent__Repositories__0__Name="main-service"
export Agent__Repositories__0__Url="git@github.com:org/repo.git"
export Agent__Repositories__0__Branch="main"
```

### Docker Compose Configuration Example

```yaml
version: '3.8'

services:
  coding-agent:
    image: coding-agent:latest
    environment:
      # Override specific values
      - ASPNETCORE_ENVIRONMENT=Production
      - Claude__ApiKey=${CLAUDE_API_KEY}
      - Agent__SessionId=${SESSION_ID}
      - Agent__Workspace__Root=/workspace
      - Git__SshKeyPath=/secrets/id_rsa
      - Orchestrator__HubUrl=http://orchestrator:5000/hub
    volumes:
      - ./workspace:/workspace
      - ~/.ssh/id_rsa:/secrets/id_rsa:ro
    ports:
      - "8080:8080"
```

### Minimal Required Configuration

The absolute minimum configuration required to start the agent:

```json
{
  "Claude": {
    "ApiKey": "sk-ant-api03-..."
  },
  
  "Agent": {
    "SessionId": "unique-session-id",
    "Repositories": [
      {
        "Name": "my-repo",
        "Url": "git@github.com:user/repo.git",
        "Branch": "main"
      }
    ],
    "Workspace": {
      "Root": "/workspace"
    }
  },
  
  "Git": {
    "SshKeyPath": "/path/to/id_rsa"
  }
}
```

All other values will use their defaults.

---

## Appendix D: Environment Variable Summary

## Appendix D: Configuration Reference Table

### Configuration Value Reference

| Configuration Path | Type | Required | Default | Environment Variable Override |
|-------------------|------|----------|---------|------------------------------|
| Claude:ApiKey | String | Yes | - | CLAUDE__APIKEY |
| Claude:Model | String | No | claude-3-5-sonnet-20241022 | CLAUDE__MODEL |
| Claude:MaxTokens | Integer | No | 4096 | CLAUDE__MAXTOKENS |
| Claude:Temperature | Decimal | No | 0.3 | CLAUDE__TEMPERATURE |
| Agent:SessionId | String | Yes | - | AGENT__SESSIONID |
| Agent:MaxExecutionMinutes | Integer | No | 30 | AGENT__MAXEXECUTIONMINUTES |
| Agent:AutoCommit | Boolean | No | true | AGENT__AUTOCOMMIT |
| Agent:AutoPush | Boolean | No | false | AGENT__AUTOPUSH |
| Agent:Repositories | Array | Yes | - | AGENT__REPOSITORIES__0__NAME, etc. |
| Agent:Workspace:Root | String | Yes | - | AGENT__WORKSPACE__ROOT |
| Git:SshKeyPath | String | Yes* | - | GIT__SSHKEYPATH |
| Git:SshKeyBase64 | String | Yes* | - | GIT__SSHKEYBASE64 |
| Git:TargetBranchPrefix | String | No | agent/ | GIT__TARGETBRANCHPREFIX |
| Git:CommitAuthorName | String | No | Code Agent | GIT__COMMITAUTHORNAME |
| Git:CommitAuthorEmail | String | No | agent@codeagent.local | GIT__COMMITAUTHOREMAIL |
| Orchestrator:HubUrl | String | No | null | ORCHESTRATOR__HUBURL |
| Orchestrator:ApiKey | String | No | null | ORCHESTRATOR__APIKEY |
| Security:AllowedCommands | Array | No | [default list] | SECURITY__ALLOWEDCOMMANDS__0, etc. |
| Security:FileSizeLimitMB | Integer | No | 10 | SECURITY__FILESIZELIMITMB |

*One of Git:SshKeyPath or Git:SshKeyBase64 is required

---

## Appendix E: API Endpoint Summary

| Endpoint | Method | Purpose |
|----------|--------|---------|
| /api/execute | POST | Submit initial task instruction |
| /api/message | POST | Send follow-up message |
| /api/status | GET | Query current agent status |
| /api/conversation | GET | Retrieve conversation history |
| /api/health | GET | Health check |
| /api/stop | POST | Stop/pause agent |

---

## Appendix F: Repository Configuration Structure

### Repository Configuration Object Schema

Each repository in the `Agent:Repositories` array must follow this structure:

```json
{
  "Name": "string",      // Required: Unique name for this repository (used as directory name)
  "Url": "string",       // Required: Git SSH URL (e.g., git@github.com:org/repo.git)
  "Branch": "string"     // Required: Branch to checkout after clone
}
```

### Single Repository Example

```json
{
  "Agent": {
    "Repositories": [
      {
        "Name": "main-service",
        "Url": "git@github.com:myorg/main-service.git",
        "Branch": "develop"
      }
    ]
  }
}
```

### Multi-Repository Example

```json
{
  "Agent": {
    "Repositories": [
      {
        "Name": "main-service",
        "Url": "git@github.com:myorg/main-service.git",
        "Branch": "develop"
      },
      {
        "Name": "shared-library",
        "Url": "git@github.com:myorg/shared-lib.git",
        "Branch": "main"
      },
      {
        "Name": "api-contracts",
        "Url": "git@github.com:myorg/api-contracts.git",
        "Branch": "main"
      }
    ]
  }
}
```

### Environment Variable Override for Repositories

```bash
# First repository
export Agent__Repositories__0__Name="main-service"
export Agent__Repositories__0__Url="git@github.com:org/main.git"
export Agent__Repositories__0__Branch="develop"

# Second repository
export Agent__Repositories__1__Name="shared-lib"
export Agent__Repositories__1__Url="git@github.com:org/shared.git"
export Agent__Repositories__1__Branch="main"

# Third repository
export Agent__Repositories__2__Name="api-contracts"
export Agent__Repositories__2__Url="git@github.com:org/contracts.git"
export Agent__Repositories__2__Branch="main"
```

### Validation Rules

**REPO-VALIDATION-001:** Repository Name
- Must be unique within the Agent:Repositories array
- Must be a valid directory name (no special characters: / \ : * ? " < > |)
- Recommended: Use kebab-case (lowercase with hyphens)

**REPO-VALIDATION-002:** Repository URL
- Must be SSH format: git@{host}:{owner}/{repo}.git
- HTTPS URLs are not supported (SSH key authentication only)
- Must be accessible from agent container network

**REPO-VALIDATION-003:** Branch
- Must be a valid Git branch name
- Branch must exist in remote repository
- Will be checked out after clone

---

## Document Revision History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2025-10-16 | Initial | Initial functional requirements document |

---

**END OF REQUIREMENTS DOCUMENT**
