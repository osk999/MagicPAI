# MagicPAI — Complete Project Plan

> A brand-new Elsa Workflows 3 native project for AI agent orchestration.
> Built from scratch. Uses Elsa's existing components wherever possible.
> Copies only essential code from MagicPrompt.

---

## Table of Contents

1. [Vision & Principles](#1-vision--principles)
2. [Architecture Overview](#2-architecture-overview)
3. [Solution Structure](#3-solution-structure)
4. [Docker Isolation Strategy](#4-docker-isolation-strategy)
5. [Elsa Native Components (What We Use As-Is)](#5-elsa-native-components)
6. [Custom Activities (What We Build)](#6-custom-activities)
7. [Code Reuse from MagicPrompt (What We Copy)](#7-code-reuse-from-magicprompt)
8. [Phase 1 — Elsa Server + Studio](#8-phase-1--elsa-server--studio)
9. [Phase 2 — Execution Environment (Docker)](#9-phase-2--execution-environment-docker)
10. [Phase 3 — AI Agent Activities](#10-phase-3--ai-agent-activities)
11. [Phase 4 — Verification Pipeline Activities](#11-phase-4--verification-pipeline-activities)
12. [Phase 5 — Orchestration Workflows](#12-phase-5--orchestration-workflows)
13. [Phase 6 — Frontend (Blazor WASM)](#13-phase-6--frontend-blazor-wasm--extends-elsa-studio)
14. [Phase 7 — Multi-Agent Support](#14-phase-7--multi-agent-support)
15. [Phase 8 — Advanced Features](#15-phase-8--advanced-features)
16. [Docker Images](#16-docker-images)
17. [API & Event Contract](#17-api--event-contract)
18. [Database Schema](#18-database-schema)
19. [Configuration](#19-configuration)
20. [Testing Strategy](#20-testing-strategy)
21. [Deployment](#21-deployment)
22. [Complete File Manifest](#22-complete-file-manifest)
23. [Implementation Timeline](#23-implementation-timeline)

---

## 1. Vision & Principles

### What is MagicPAI?

MagicPAI is a visual AI agent orchestration platform built natively on Elsa Workflows 3.
Users drag-and-drop workflow nodes in a visual designer to create AI coding pipelines that:

- Triage incoming prompts (simple vs complex)
- Decompose complex tasks into parallel sub-tasks (Architect)
- Execute sub-tasks via AI CLI agents (Claude Code, Codex, Gemini CLI)
- Verify results through quality gates (compile, test, security, coverage)
- Repair failures automatically (AI feedback loops)
- Merge results and deliver

### Design Principles

1. **Elsa-Native First** — Use Elsa's built-in activities for control flow, persistence,
   variables, parallel execution, sub-workflows, bookmarks, and the visual designer.
   Only build custom activities for AI-specific logic.

2. **From Scratch** — No architectural debt from MagicPrompt. Clean interfaces, clean DI,
   clean project structure. Copy only standalone utility code that has no alternatives.

3. **Docker-Isolated Execution** — Each workflow execution (or each worker within a workflow)
   runs in its own Docker container. The Elsa server orchestrates containers; it never
   executes user code in its own process.

4. **Multi-Agent** — First-class support for Claude Code, OpenAI Codex CLI, Google Gemini CLI,
   and any future CLI agent. Agent-agnostic interfaces.

5. **Visual-First** — The primary user experience is Elsa Studio. Users build, modify, and
   monitor workflows visually. Code-first workflows are for power users and built-in templates.

### What Comes From MagicPrompt vs What's New

| Capability | Source |
|---|---|
| Workflow engine, state, persistence, variables | **Elsa 3** (native) |
| Visual designer (drag-drop) | **Elsa Studio** (native) |
| Control flow (if, switch, loops, parallel, fork/join) | **Elsa 3** (native) |
| Sub-workflows, composition | **Elsa 3** (native) |
| Bookmarks, human approval | **Elsa 3** (native) |
| HTTP triggers, webhooks | **Elsa 3** (native) |
| Workflow versioning | **Elsa 3** (native) |
| REST API for workflow CRUD | **Elsa 3** (native) |
| Identity & auth | **Elsa 3** (native) |
| AI CLI agent execution | **New** (custom activities, copied runner logic) |
| Verification gates | **Copied** from MagicPrompt (standalone gate classes) |
| Docker container management | **New** (Docker.DotNet based) |
| Real-time output streaming | **New** (SignalR hub + Elsa event handlers) |
| Triage / complexity classification | **New** (custom activity, copied prompt logic) |
| Architect / task decomposition | **New** (custom activity, copied prompt logic) |
| Git worktree isolation | **Copied** from MagicPrompt (standalone) |
| Feedback/repair loops | **Elsa While** + custom repair activity |
| SharedBlackboard (file claims) | **Copied** from MagicPrompt (ConcurrentDictionary) |
| Blazor frontend | **New** (integrated into Elsa Studio, single Blazor app) |

---

## 2. Architecture Overview

```
┌──────────────────────────────────────────────────────────┐
│                    BROWSER                                │
│  ┌──────────────────────────────────────────────────┐    │
│  │  MagicPAI Studio (Blazor WASM — Single App)      │    │
│  │                                                   │    │
│  │  ┌─────────────┐  ┌──────────────────────────┐   │    │
│  │  │ Custom Pages │  │ Elsa Studio (built-in)   │   │    │
│  │  │ - Dashboard  │  │ - Drag-drop designer     │   │    │
│  │  │ - Sessions   │  │ - Activity palette       │   │    │
│  │  │ - Live output│  │ - Workflow monitoring    │   │    │
│  │  │ - Cost view  │  │ - Version management     │   │    │
│  │  │ - DAG view   │  │ - Incident viewer        │   │    │
│  │  └──────────────┘  └──────────────────────────┘   │    │
│  │                                                   │    │
│  │  Shared: SignalR client, auth, navigation         │    │
│  └─────────────────────┬─────────────────────────────┘    │
│                        │ SignalR + REST API                │
└────────────────────────┼──────────────────────────────────┘
                         │
┌────────────────────────▼──────────────────────────────┐
│              MagicPAI.Server (ASP.NET Core)            │
│                                                        │
│  ┌──────────────┐  ┌───────────────┐  ┌─────────────┐ │
│  │ SignalR Hub   │  │ Elsa Runtime  │  │ Elsa API    │ │
│  │ (SessionHub)  │  │ (workflows)   │  │ (CRUD)      │ │
│  └──────┬───────┘  └───────┬───────┘  └─────────────┘ │
│         │                  │                           │
│  ┌──────▼──────────────────▼───────────────────────┐   │
│  │              Custom Activities                   │   │
│  │  ┌──────────┐ ┌──────────┐ ┌────────────────┐   │   │
│  │  │ AI Agent │ │ Verify   │ │ Docker Manager │   │   │
│  │  │ Runner   │ │ Gates    │ │ (spawn/manage) │   │   │
│  │  └──────────┘ └──────────┘ └───────┬────────┘   │   │
│  └────────────────────────────────────┼────────────┘   │
│                                       │                │
│  ┌────────────────────────────────────▼────────────┐   │
│  │           Container Orchestrator                 │   │
│  │  Docker.DotNet → spawn worker containers        │   │
│  └────────────────────────────────────┬────────────┘   │
│                                       │                │
└───────────────────────────────────────┼────────────────┘
                                        │ Docker API
                    ┌───────────────────┼───────────────────┐
                    │                   │                   │
          ┌─────────▼──────┐  ┌────────▼───────┐  ┌───────▼────────┐
          │ Worker Container│  │Worker Container│  │Worker Container│
          │ (magicpai-env)  │  │(magicpai-env)  │  │(magicpai-env)  │
          │                 │  │                │  │                │
          │ - Claude Code   │  │ - Codex CLI    │  │ - Claude Code  │
          │ - Node.js       │  │ - Node.js      │  │ - Gemini CLI   │
          │ - .NET SDK      │  │ - .NET SDK     │  │ - Python       │
          │ - Python        │  │ - Python       │  │ - Rust         │
          │ - Playwright    │  │ - Playwright   │  │ - Go           │
          │ - Git worktree  │  │ - Git worktree │  │ - Git worktree │
          │ - /workspace    │  │ - /workspace   │  │ - /workspace   │
          └─────────────────┘  └────────────────┘  └────────────────┘
```

### Key Architecture Decisions

1. **Elsa runs in the server process only** — It manages workflow state, persistence,
   the visual designer, and activity scheduling. It does NOT run user code.

2. **Worker containers are ephemeral** — Spawned per-task (or per-workflow), destroyed
   when done. The `magicpai-env` Docker image has all tools pre-installed.

3. **Communication: Server ↔ Container** — Via Docker exec commands (shell commands
   routed through Docker.DotNet). Output streamed via `docker logs --follow` or
   exec stdout capture.

4. **State lives in Elsa** — Workflow variables, activity inputs/outputs, and the
   persistence layer are all Elsa-native. No custom DataBus or WorkflowExecutor.

5. **SharedBlackboard is a DI singleton** — Injected into activities for file claim
   coordination across parallel workers. Scoped per workflow instance.

---

## 3. Solution Structure

```
MagicPAI/
├── MagicPAI.sln
│
├── MagicPAI.Server/                        # ASP.NET Core host
│   ├── Program.cs                          # DI, Elsa config, SignalR, middleware
│   ├── appsettings.json                    # Config + Elsa identity
│   ├── Hubs/
│   │   └── SessionHub.cs                   # SignalR hub (simplified, event bridge)
│   ├── Bridge/
│   │   ├── ElsaEventBridge.cs              # Elsa notifications → SignalR
│   │   └── WorkflowProgressTracker.cs      # Track workflow execution progress
│   └── Controllers/
│       └── SessionController.cs            # REST API for session management
│
├── MagicPAI.Activities/                    # Custom Elsa activities
│   ├── MagicPAI.Activities.csproj
│   ├── AI/
│   │   ├── RunCliAgentActivity.cs          # Generic CLI agent runner
│   │   ├── TriageActivity.cs               # Complexity classification
│   │   ├── ArchitectActivity.cs            # Task decomposition → Elsa sub-workflow
│   │   └── ModelRouterActivity.cs          # Adaptive model selection
│   ├── Verification/
│   │   ├── RunVerificationActivity.cs      # Execute verification pipeline
│   │   ├── RepairActivity.cs               # AI-generated repair
│   │   └── Gates/                          # Individual gates (copied from MP)
│   │       ├── CompileGate.cs
│   │       ├── TestGate.cs
│   │       ├── CoverageGate.cs
│   │       ├── SecurityGate.cs
│   │       ├── LintGate.cs
│   │       ├── HallucinationDetector.cs
│   │       └── QualityReviewGate.cs
│   ├── Docker/
│   │   ├── SpawnContainerActivity.cs       # Start worker container
│   │   ├── ExecInContainerActivity.cs      # Run command in container
│   │   ├── DestroyContainerActivity.cs     # Cleanup container
│   │   └── StreamContainerLogsActivity.cs  # Stream output from container
│   └── Git/
│       ├── CreateWorktreeActivity.cs       # Git worktree isolation
│       ├── MergeWorktreeActivity.cs        # Merge branch back
│       └── CleanupWorktreeActivity.cs      # Delete worktree
│
├── MagicPAI.Core/                          # Shared models, interfaces, utilities
│   ├── MagicPAI.Core.csproj
│   ├── Models/
│   │   ├── CliAgentResponse.cs             # Agent execution result
│   │   ├── VerificationResult.cs           # Gate results
│   │   ├── GateResult.cs                   # Individual gate outcome
│   │   └── TriageResult.cs                 # Triage classification
│   ├── Services/
│   │   ├── ICliAgentRunner.cs              # Interface: run any AI CLI agent
│   │   ├── ClaudeRunner.cs                 # Claude Code CLI execution (copied+cleaned)
│   │   ├── CodexRunner.cs                  # OpenAI Codex CLI execution
│   │   ├── GeminiRunner.cs                 # Google Gemini CLI execution
│   │   ├── IContainerManager.cs            # Interface: Docker container lifecycle
│   │   ├── DockerContainerManager.cs       # Docker.DotNet implementation
│   │   ├── SharedBlackboard.cs             # File claims (copied from MP)
│   │   ├── WorktreeManager.cs              # Git worktree operations (copied from MP)
│   │   └── VerificationPipeline.cs         # Gate chain execution (copied+cleaned)
│   └── Config/
│       └── MagicPaiConfig.cs               # Simplified configuration
│
├── MagicPAI.Workflows/                     # Built-in workflow templates
│   ├── MagicPAI.Workflows.csproj
│   ├── FullOrchestrateWorkflow.cs          # Main orchestration pipeline
│   ├── SimpleAgentWorkflow.cs              # Single-agent execution
│   ├── VerifyAndRepairWorkflow.cs          # Verification + feedback loop
│   └── Templates/                          # JSON workflow templates for Studio
│       ├── full-orchestrate.json
│       ├── simple-agent.json
│       └── verify-repair.json
│
├── MagicPAI.Studio/                        # Blazor WASM frontend (extends Elsa Studio)
│   ├── MagicPAI.Studio.csproj
│   ├── Program.cs                          # Elsa Studio + custom pages registration
│   ├── wwwroot/
│   │   ├── index.html                      # Blazor WASM host page
│   │   └── css/app.css                     # Custom styles
│   ├── Layout/
│   │   └── MainLayout.razor               # Shared layout (nav, sidebar)
│   ├── Pages/
│   │   ├── Dashboard.razor                 # Session list + quick start
│   │   ├── SessionView.razor              # Active session: live output + DAG
│   │   ├── CostDashboard.razor            # Token usage + cost analytics
│   │   └── Settings.razor                  # Agent config, model routing
│   ├── Components/
│   │   ├── OutputPanel.razor              # Real-time streaming text output
│   │   ├── DagView.razor                  # Visual DAG progress view
│   │   ├── VerificationBadge.razor        # Gate pass/fail indicators
│   │   ├── AgentSelector.razor            # Claude/Codex/Gemini picker
│   │   ├── CostTracker.razor              # Live cost tracking
│   │   └── ContainerStatus.razor          # Docker container health
│   └── Services/
│       ├── SessionHubClient.cs            # Type-safe SignalR client (C#)
│       └── SessionApiClient.cs            # REST API client
│
├── MagicPAI.Tests/                         # xUnit tests
│   ├── MagicPAI.Tests.csproj
│   ├── Activities/
│   │   ├── RunCliAgentActivityTests.cs
│   │   ├── TriageActivityTests.cs
│   │   └── VerificationActivityTests.cs
│   ├── Services/
│   │   ├── ClaudeRunnerTests.cs
│   │   ├── DockerContainerManagerTests.cs
│   │   └── SharedBlackboardTests.cs
│   └── Workflows/
│       └── FullOrchestrateWorkflowTests.cs
│
└── docker/
    ├── docker-compose.yml                  # Full stack: server + studio + postgres
    ├── docker-compose.dev.yml              # Dev overrides
    ├── server/
    │   └── Dockerfile                      # MagicPAI.Server image
    └── worker-env/
        ├── Dockerfile                      # magicpai-env worker image
        └── entrypoint.sh                   # Worker container entrypoint
```

---

## 4. Docker Isolation Strategy

### 4.1 Architecture: Server Container + Worker Containers

```
┌───────────────────────────────────────────────────────┐
│  Docker Host                                          │
│                                                       │
│  ┌─────────────────────────────────────────────────┐  │
│  │  magicpai-server (always running)               │  │
│  │  - ASP.NET Core + Elsa Runtime                  │  │
│  │  - Elsa Studio (Blazor WASM)                    │  │
│  │  - SignalR Hub                                  │  │
│  │  - PostgreSQL (or SQLite for dev)               │  │
│  │  - Docker socket mounted (/var/run/docker.sock) │  │
│  │  - Manages worker containers via Docker.DotNet  │  │
│  └──────────────────┬──────────────────────────────┘  │
│                     │ docker.sock                     │
│     ┌───────────────┼───────────────┐                 │
│     ▼               ▼               ▼                 │
│  ┌──────┐       ┌──────┐       ┌──────┐              │
│  │Worker│       │Worker│       │Worker│              │
│  │  #1  │       │  #2  │       │  #3  │              │
│  │      │       │      │       │      │              │
│  │claude│       │codex │       │claude│              │
│  │code  │       │ CLI  │       │code  │              │
│  └──────┘       └──────┘       └──────┘              │
│  (ephemeral)    (ephemeral)    (ephemeral)            │
└───────────────────────────────────────────────────────┘
```

### 4.2 How It Works

1. **Elsa workflow reaches a `SpawnContainerActivity`**
   - Activity uses `Docker.DotNet` to create a new container from `magicpai-env` image
   - Mounts the project workspace as a volume
   - Optionally creates a git worktree for isolation
   - Returns container ID to Elsa workflow variable

2. **Subsequent activities use `ExecInContainerActivity`**
   - Runs commands inside the container via `docker exec`
   - Streams stdout/stderr back to Elsa execution log → SignalR → browser
   - Captures exit codes and output for gate evaluation

3. **AI agent runs inside the container**
   - `claude --dangerously-skip-permissions -p "..." --output-format stream-json`
   - Streaming JSON parsed line-by-line, forwarded as output chunks
   - Cost data extracted from final JSON message

4. **Verification gates run inside the same container**
   - `dotnet build`, `npm test`, `cargo check`, etc.
   - Gate results passed back as Elsa activity outputs

5. **Container destroyed by `DestroyContainerActivity`**
   - Or auto-destroyed on workflow completion via Elsa's activity lifecycle

### 4.3 Container Lifecycle Activity Pattern

```csharp
// Elsa Flowchart for a single task execution:

SpawnContainer(image: "magicpai-env", workspace: "/projects/foo")
    ↓
CreateWorktree(containerId, branch: "task-{id}")
    ↓
RunCliAgent(containerId, agent: "claude", prompt: "...")
    ↓ [output stored in Elsa variable]
RunVerification(containerId, workDir: "/workspace")
    ↓ Fork: Passed / Failed
    │
    ├── [Passed] → MergeWorktree(containerId) → DestroyContainer
    │
    └── [Failed] → While(attempts < maxRepair)
                      → RepairActivity(containerId, failedGates)
                      → RunVerification(containerId)
                      → If(passed) → Break
                   → MergeWorktree or Rollback
                   → DestroyContainer
```

### 4.4 Container Configuration

```csharp
// MagicPAI.Core/Services/DockerContainerManager.cs

public class ContainerConfig
{
    public string Image { get; set; } = "magicpai-env:latest";
    public string WorkspacePath { get; set; } = "";     // Host path to mount
    public string ContainerWorkDir { get; set; } = "/workspace";
    public int MemoryLimitMb { get; set; } = 4096;
    public int CpuCount { get; set; } = 2;
    public bool MountDockerSocket { get; set; } = false; // For Docker-in-Docker
    public bool EnableGui { get; set; } = false;         // noVNC for Playwright
    public int? GuiPort { get; set; }                    // External noVNC port
    public Dictionary<string, string> Env { get; set; } = new();
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(30);
}
```

### 4.5 Alternative: Local Execution (No Docker)

For development or lightweight use, activities can also run locally:

```csharp
public interface IExecutionEnvironment
{
    Task<string> RunCommandAsync(string command, string workDir, CancellationToken ct);
    Task<Process> StartProcessAsync(ProcessStartInfo psi, CancellationToken ct);
    string Kind { get; } // "docker" or "local"
}

// DI registration based on config:
if (config.UseDocker)
    services.AddScoped<IExecutionEnvironment, DockerExecutionEnvironment>();
else
    services.AddScoped<IExecutionEnvironment, LocalExecutionEnvironment>();
```

---

## 5. Elsa Native Components (What We Use As-Is)

These are Elsa 3 built-in capabilities we use directly — NO custom code needed:

### 5.1 Control Flow (replaces MagicPrompt's custom node types)

| MagicPrompt Node Type | Elsa Built-In | How It Works |
|---|---|---|
| `if-condition` | `If` activity | `Condition` → `Then` / `Else` branches |
| `router` | `Switch` activity | Multi-way branching on expression |
| `loop` | `For` activity | Counted iteration |
| `whileLoop` | `While` activity | Condition-based loop (perfect for repair loops!) |
| `forEach` | `ForEach` activity | Iterate list with `CurrentValue`/`CurrentIndex` |
| `parallel` | `Fork` + `Join` | Fan-out/fan-in with WaitAll or WaitAny |
| `group` | `Sequence` / `Flowchart` | Container for sequential/graph activities |
| `delay` | `Delay` activity | Timer-based pause |
| `merge-join` | `Join` activity | Synchronize parallel branches |
| `sub-workflow` | `DispatchWorkflow` | Call child workflow with input/output |
| `human-approval` | Bookmark + `RunTask` | Suspend workflow, await human action |
| `error-handler` | Incident Strategy | Configurable fault handling |

### 5.2 Workflow Management (replaces custom WorkflowExecutor)

| Feature | Elsa Native |
|---|---|
| Workflow definitions (JSON) | `IWorkflowDefinitionStore` |
| Workflow instances (state) | `IWorkflowInstanceStore` |
| Variables | `builder.WithVariable<T>()` |
| Execution persistence | EF Core (SQLite, PostgreSQL, SQL Server) |
| Versioning | Auto-incrementing versions, latest/published flags |
| Resumption (crash recovery) | Bookmark system + instance persistence |
| Expression evaluation | C#, JavaScript, Liquid, Python |

### 5.3 API & Identity (replaces custom REST endpoints)

| Feature | Elsa Native |
|---|---|
| Workflow CRUD | `/elsa/api/workflow-definitions` |
| Instance management | `/elsa/api/workflow-instances` |
| Execution triggers | `/elsa/api/workflow-definitions/{id}/execute` |
| Identity & auth | `Elsa.Identity` package with config-based users/roles |
| HTTP triggers | `HttpEndpoint` activity (path-based workflow start) |
| Webhooks | `Elsa.Webhooks` package |

### 5.4 Visual Designer (replaces custom DAG editor)

| Feature | Elsa Studio |
|---|---|
| Drag-drop workflow building | Flowchart canvas with auto-layout |
| Activity palette | Categories, search, custom activities auto-appear |
| Property editor | Input/output binding with expression languages |
| Execution monitoring | Real-time status, incident viewer |
| Version management | Publish, revert, compare versions |
| Drill-into composites | Edit sub-workflows inline |

### 5.5 Data Flow Between Activities (replaces DataBus)

Elsa provides three mechanisms:

```csharp
// 1. Workflow Variables (shared across all activities)
var response = builder.WithVariable<string>("AgentResponse", "");

// 2. Activity Output → Input binding
new MyActivity { Input = new(ctx => previousActivity.GetOutput<string>(ctx)) }

// 3. Direct output access by activity ID
new MyActivity { Input = new(ctx => ctx.GetOutput("ActivityId1", "OutputName")) }
```

---

## 6. Custom Activities (What We Build)

### 6.1 Activity Categories

```
MagicPAI/
├── AI Agents/
│   ├── RunCliAgent          # Execute any CLI AI agent in a container
│   ├── Triage               # Classify prompt complexity (Haiku)
│   ├── Architect            # Decompose into task DAG (Opus)
│   └── ModelRouter          # Select best model for task
│
├── Docker/
│   ├── SpawnContainer       # Create worker container
│   ├── ExecInContainer      # Run shell command in container
│   ├── StreamFromContainer  # Stream real-time output
│   └── DestroyContainer     # Cleanup container
│
├── Verification/
│   ├── RunVerification      # Execute gate chain
│   ├── Repair               # AI-generated code fix
│   └── (individual gates are services, not activities)
│
├── Git/
│   ├── CreateWorktree       # Branch isolation
│   ├── MergeWorktree        # Merge back to main
│   └── CleanupWorktree      # Remove worktree
│
└── Infrastructure/
    ├── EmitOutputChunk      # Send streaming text to SignalR
    ├── UpdateCost           # Track token costs
    └── ClaimFile            # Atomic file ownership (blackboard)
```

### 6.2 RunCliAgent Activity (The Core Activity)

```csharp
// MagicPAI.Activities/AI/RunCliAgentActivity.cs

[Activity("MagicPAI", "AI Agents",
    "Execute a prompt via an AI CLI agent (Claude, Codex, Gemini) in a Docker container")]
[FlowNode("Done", "Failed")]
public class RunCliAgentActivity : Activity
{
    [Input(DisplayName = "Agent",
        UIHint = InputUIHints.DropDown,
        Options = new[] { "claude", "codex", "gemini" },
        Description = "Which AI CLI agent to use")]
    public Input<string> Agent { get; set; } = new("claude");

    [Input(DisplayName = "Prompt", UIHint = InputUIHints.MultiLine)]
    public Input<string> Prompt { get; set; } = default!;

    [Input(DisplayName = "Container ID",
        Description = "Docker container to execute in")]
    public Input<string> ContainerId { get; set; } = default!;

    [Input(DisplayName = "Working Directory")]
    public Input<string> WorkingDirectory { get; set; } = new("/workspace");

    [Input(DisplayName = "Model",
        UIHint = InputUIHints.DropDown,
        Options = new[] { "auto", "haiku", "sonnet", "opus",
                          "gpt-4o", "o3", "gemini-2.5-pro" },
        Category = "Model")]
    public Input<string> Model { get; set; } = new("auto");

    [Input(DisplayName = "Max Turns", Category = "Limits")]
    public Input<int> MaxTurns { get; set; } = new(20);

    [Input(DisplayName = "Timeout (minutes)", Category = "Limits")]
    public Input<int> TimeoutMinutes { get; set; } = new(30);

    [Output(DisplayName = "Response")]
    public Output<string> Response { get; set; } = default!;

    [Output(DisplayName = "Success")]
    public Output<bool> Success { get; set; } = default!;

    [Output(DisplayName = "Cost USD")]
    public Output<decimal> CostUsd { get; set; } = default!;

    [Output(DisplayName = "Files Modified")]
    public Output<string[]> FilesModified { get; set; } = default!;

    [Output(DisplayName = "Exit Code")]
    public Output<int> ExitCode { get; set; } = default!;

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var containerMgr = context.GetRequiredService<IContainerManager>();
        var agentFactory = context.GetRequiredService<ICliAgentFactory>();

        var containerId = ContainerId.Get(context);
        var agent = agentFactory.Create(Agent.Get(context));
        var prompt = Prompt.Get(context);
        var model = Model.Get(context);
        var workDir = WorkingDirectory.Get(context);
        var maxTurns = MaxTurns.Get(context);

        // Build CLI command for the agent
        var command = agent.BuildCommand(prompt, model, maxTurns, workDir);

        // Execute in container with streaming output
        var result = await containerMgr.ExecStreamingAsync(
            containerId, command,
            onOutput: chunk =>
            {
                // Log to Elsa execution log (picked up by SignalR bridge)
                context.AddExecutionLogEntry("OutputChunk",
                    System.Text.Json.JsonSerializer.Serialize(new
                    {
                        activityId = context.Activity.Id,
                        text = chunk
                    }));
            },
            timeout: TimeSpan.FromMinutes(TimeoutMinutes.Get(context)),
            ct: context.CancellationToken);

        // Parse agent response
        var parsed = agent.ParseResponse(result.Output);

        Response.Set(context, parsed.Output);
        Success.Set(context, parsed.Success);
        CostUsd.Set(context, parsed.CostUsd);
        FilesModified.Set(context, parsed.FilesModified);
        ExitCode.Set(context, result.ExitCode);

        await context.CompleteActivityWithOutcomesAsync(
            parsed.Success ? "Done" : "Failed");
    }
}
```

### 6.3 SpawnContainer Activity

```csharp
[Activity("MagicPAI", "Docker",
    "Spawn an isolated Docker container for task execution")]
public class SpawnContainerActivity : Activity
{
    [Input(DisplayName = "Image")]
    public Input<string> Image { get; set; } = new("magicpai-env:latest");

    [Input(DisplayName = "Workspace Path",
        Description = "Host path to mount as /workspace")]
    public Input<string> WorkspacePath { get; set; } = default!;

    [Input(DisplayName = "Memory Limit (MB)", Category = "Resources")]
    public Input<int> MemoryLimitMb { get; set; } = new(4096);

    [Input(DisplayName = "Enable GUI (noVNC)", Category = "Features")]
    public Input<bool> EnableGui { get; set; } = new(false);

    [Input(DisplayName = "Environment Variables",
        UIHint = InputUIHints.JsonEditor, Category = "Advanced")]
    public Input<Dictionary<string, string>?> EnvVars { get; set; } = default!;

    [Output(DisplayName = "Container ID")]
    public Output<string> ContainerId { get; set; } = default!;

    [Output(DisplayName = "GUI URL")]
    public Output<string?> GuiUrl { get; set; } = default!;

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var docker = context.GetRequiredService<IContainerManager>();

        var config = new ContainerConfig
        {
            Image = Image.Get(context),
            WorkspacePath = WorkspacePath.Get(context),
            MemoryLimitMb = MemoryLimitMb.Get(context),
            EnableGui = EnableGui.Get(context),
            Env = EnvVars.GetOrDefault(context) ?? new()
        };

        var result = await docker.SpawnAsync(config, context.CancellationToken);

        ContainerId.Set(context, result.ContainerId);
        GuiUrl.Set(context, result.GuiUrl);

        await context.CompleteActivityAsync();
    }
}
```

### 6.4 RunVerification Activity

```csharp
[Activity("MagicPAI", "Verification",
    "Run verification gates (compile, test, security, etc.) in a container")]
[FlowNode("Passed", "Failed", "Inconclusive")]
public class RunVerificationActivity : Activity
{
    [Input(DisplayName = "Container ID")]
    public Input<string> ContainerId { get; set; } = default!;

    [Input(DisplayName = "Working Directory")]
    public Input<string> WorkingDirectory { get; set; } = new("/workspace");

    [Input(DisplayName = "Gates to Run",
        UIHint = InputUIHints.CheckList,
        Options = new[] { "compile", "test", "coverage", "security",
                          "lint", "hallucination", "quality" },
        Description = "Which verification gates to execute")]
    public Input<string[]> Gates { get; set; } =
        new(new[] { "compile", "test", "hallucination" });

    [Input(DisplayName = "Worker Output (for hallucination check)",
        Category = "Context")]
    public Input<string?> WorkerOutput { get; set; } = default!;

    [Output(DisplayName = "All Passed")]
    public Output<bool> AllPassed { get; set; } = default!;

    [Output(DisplayName = "Failed Gates")]
    public Output<string[]> FailedGates { get; set; } = default!;

    [Output(DisplayName = "Gate Results (JSON)")]
    public Output<string> GateResultsJson { get; set; } = default!;

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var containerMgr = context.GetRequiredService<IContainerManager>();
        var pipeline = context.GetRequiredService<VerificationPipeline>();

        var containerId = ContainerId.Get(context);
        var workDir = WorkingDirectory.Get(context);
        var gates = Gates.Get(context);

        var result = await pipeline.RunAsync(
            containerId, workDir, gates,
            WorkerOutput.GetOrDefault(context),
            context.CancellationToken);

        AllPassed.Set(context, result.AllPassed);
        FailedGates.Set(context,
            result.Gates.Where(g => !g.Passed).Select(g => g.Name).ToArray());
        GateResultsJson.Set(context,
            System.Text.Json.JsonSerializer.Serialize(result.Gates));

        var outcome = result.IsInconclusive ? "Inconclusive"
            : result.AllPassed ? "Passed" : "Failed";
        await context.CompleteActivityWithOutcomesAsync(outcome);
    }
}
```

---

## 7. Code Reuse from MagicPrompt (What We Copy)

### 7.1 Copy As-Is (Standalone Code)

| Source File | Destination | Why |
|---|---|---|
| `SharedBlackboard.cs` | `MagicPAI.Core/Services/` | Pure ConcurrentDictionary, no MP deps |
| `WorktreeManager.cs` | `MagicPAI.Core/Services/` | Pure git commands, no MP deps |
| `docker/env-gui/Dockerfile` | `docker/worker-env/Dockerfile` | Base worker image (add Codex/Gemini CLIs) |
| `docker/env-gui/entrypoint.sh` | `docker/worker-env/entrypoint.sh` | Container init (VNC, Docker socket) |

### 7.2 Copy and Clean (Remove MP-specific patterns)

| Source File | What to Change |
|---|---|
| `ClaudeRunner.cs` | Remove `ITabExecutionEnvironment` dependency. Make it take a shell command executor interface instead. Remove VCR cassette logic. Keep streaming JSON parsing, cost extraction, session management. |
| `VerificationPipeline.cs` | Remove `ITabExecutionEnvironment`. Make gates take `IContainerManager` + `containerId`. Keep gate chain logic, early-stop, blocking/non-blocking distinction. |
| `CompileGate.cs`, `TestGate.cs`, etc. | Change from `ITabExecutionEnvironment.RunShellCommandAsync()` to `IContainerManager.ExecAsync()`. Keep all detection logic (language detection, build system detection). |
| `HallucinationDetector.cs` | Minimal changes — file existence checks via container exec. |
| `FeedbackLoopController.cs` | Replace with Elsa `While` loop + `RepairActivity`. Copy only the repair prompt generation logic. |
| `AIRepairPromptGenerator.cs` | Copy prompt templates. Remove MP service dependencies. |
| `TriageService.cs` | Copy classification prompt. Replace ClaudeRunner call with `ICliAgentRunner`. |
| `ArchitectService.cs` | Copy decomposition prompt. Output becomes Elsa sub-workflow tasks instead of TaskDAG. |

### 7.3 Do NOT Copy (Replace with Elsa)

| MagicPrompt Code | Why Not | Elsa Replacement |
|---|---|---|
| `WorkflowExecutor.cs` | Elsa IS the workflow executor | Elsa Workflow Runtime |
| `WorkflowDefinition.cs` | Elsa has its own workflow model | Elsa JSON workflow definitions |
| `WorkflowNodeRegistry.cs` | 80+ node types → Elsa activities | `AddActivitiesFrom<>()` |
| `DataBus.cs` | Elsa has native variable/output system | `builder.WithVariable<T>()` |
| `IWorkflowNodeHandler.cs` | Handler interface replaced by Activities | `Activity` base class |
| `TaskDAG.cs` / `TaskNode.cs` | Elsa Flowchart IS the DAG | Elsa Flowchart activity |
| `DAGScheduler.cs` | Elsa handles scheduling | Elsa execution runtime |
| `DependencyResolver.cs` | Elsa Flowchart connections = deps | Elsa connection model |
| `SessionHub.cs` (13 partials) | Too coupled, rebuild simplified | New `SessionHub.cs` (~200 lines) |
| `OrchestrationConfig.cs` (293 props) | Overengineered, simplify | `MagicPaiConfig.cs` (~50 props) |
| `OrchestrationRouter.cs` | Triage routing = Elsa If/Switch | Elsa control flow |
| `SingleAgentStrategy.cs` | Simple execution = Elsa Sequence | Elsa built-in |
| `MultiAgentStrategy.cs` | DAG execution = Elsa Fork/ForEach | Elsa built-in |
| `ModelFailoverService.cs` | Rebuild simpler as activity | New `ModelRouterActivity` |
| `RateLimitCoordinator.cs` | Rebuild simpler with SemaphoreSlim | Activity-level rate limiting |
| All frontend code | Rebuild as Blazor, integrated with Elsa Studio | New Blazor WASM pages |

---

## 8. Phase 1 — Elsa Server + Studio

### 8.1 Deliverables
- `MagicPAI.Server` project with Elsa registered
- Elsa Studio accessible at `/workflows`
- PostgreSQL persistence (SQLite for dev)
- Identity configured (admin user)
- All custom activities registered and visible in Studio palette

### 8.2 Program.cs

```csharp
using Elsa.EntityFrameworkCore.Extensions;
using Elsa.EntityFrameworkCore.Modules.Management;
using Elsa.EntityFrameworkCore.Modules.Runtime;
using Elsa.Extensions;
using MagicPAI.Activities;
using MagicPAI.Core.Services;
using MagicPAI.Server.Bridge;
using MagicPAI.Server.Hubs;

var builder = WebApplication.CreateBuilder(args);

// ─── MagicPAI Services ───
builder.Services.AddSingleton<SharedBlackboard>();
builder.Services.AddSingleton<IContainerManager, DockerContainerManager>();
builder.Services.AddSingleton<ICliAgentFactory, CliAgentFactory>();
builder.Services.AddSingleton<VerificationPipeline>();
builder.Services.Configure<MagicPaiConfig>(
    builder.Configuration.GetSection("MagicPAI"));

// ─── SignalR ───
builder.Services.AddSignalR();

// ─── Elsa ───
builder.Services.AddElsa(elsa =>
{
    // Workflow Management
    elsa.UseWorkflowManagement(mgmt =>
    {
        mgmt.UseEntityFrameworkCore(ef =>
            ef.UseSqlite("Data Source=magicpai.db"));
    });

    // Workflow Runtime
    elsa.UseWorkflowRuntime(runtime =>
    {
        runtime.UseEntityFrameworkCore(ef =>
            ef.UseSqlite("Data Source=magicpai.db"));
    });

    // Identity
    elsa.UseIdentity(identity =>
        identity.UseConfigurationBasedIdentityProvider());
    elsa.UseDefaultAuthentication();

    // APIs
    elsa.UseWorkflowsApi();
    elsa.UseHttp();

    // Register ALL MagicPAI activities
    elsa.AddActivitiesFrom<RunCliAgentActivity>();

    // Register built-in workflow templates
    elsa.AddWorkflow<FullOrchestrateWorkflow>();
    elsa.AddWorkflow<SimpleAgentWorkflow>();
    elsa.AddWorkflow<VerifyAndRepairWorkflow>();
});

// ─── Elsa Event Bridge ───
builder.Services.AddScoped<ElsaEventBridge>();

// ─── CORS for dev ───
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(p =>
        p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// ─── Middleware ───
app.UseCors();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseWorkflowsApi();
app.UseWorkflows();

// ─── SignalR Hub ───
app.MapHub<SessionHub>("/hub");

// ─── Blazor WASM (MagicPAI.Studio with Elsa Studio built-in) ───
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

// ─── DB Migration ───
await app.Services.MigrateElsaDatabaseAsync();

app.Run();
```

---

## 9. Phase 2 — Execution Environment (Docker)

### 9.1 Deliverables
- `magicpai-env` Docker image (based on MagicPrompt's env-gui)
- `DockerContainerManager` service (spawn/exec/destroy/stream)
- `SpawnContainerActivity`, `ExecInContainerActivity`, `DestroyContainerActivity`
- `LocalExecutionEnvironment` fallback for non-Docker dev

### 9.2 Worker Docker Image

```dockerfile
# docker/worker-env/Dockerfile
FROM debian:bookworm

ENV DEBIAN_FRONTEND=noninteractive

# Base utilities + GUI stack
RUN apt-get update \
  && apt-get install -y --no-install-recommends \
    ca-certificates curl git gnupg unzip xz-utils \
    build-essential cmake python3 python3-pip \
    xvfb fluxbox x11vnc novnc websockify \
    fonts-liberation netcat-openbsd sudo jq \
  && rm -rf /var/lib/apt/lists/*

# Node.js 24 LTS
RUN curl -fsSL https://deb.nodesource.com/setup_24.x | bash - \
  && apt-get install -y --no-install-recommends nodejs \
  && rm -rf /var/lib/apt/lists/*

# .NET SDK 10.0 + 9.0 + 8.0
RUN curl -fsSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh \
  && chmod +x dotnet-install.sh \
  && ./dotnet-install.sh --channel 10.0 --install-dir /usr/share/dotnet \
  && ./dotnet-install.sh --channel 9.0 --install-dir /usr/share/dotnet \
  && ./dotnet-install.sh --channel 8.0 --install-dir /usr/share/dotnet \
  && rm dotnet-install.sh \
  && ln -s /usr/share/dotnet/dotnet /usr/bin/dotnet
ENV DOTNET_ROOT=/usr/share/dotnet
ENV PATH="${PATH}:/usr/share/dotnet"

# Go
RUN curl -fsSL https://go.dev/dl/go1.24.2.linux-amd64.tar.gz | tar -C /usr/local -xzf -
ENV PATH="${PATH}:/usr/local/go/bin"

# Playwright + Chromium
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
RUN mkdir -p /opt/playwright && cd /opt/playwright \
  && npm init -y && npm install playwright \
  && npx playwright install --with-deps chromium

# Docker CLI (sibling container management)
RUN install -m 0755 -d /etc/apt/keyrings \
  && curl -fsSL https://download.docker.com/linux/debian/gpg \
     -o /etc/apt/keyrings/docker.asc \
  && echo "deb [arch=$(dpkg --print-architecture) \
     signed-by=/etc/apt/keyrings/docker.asc] \
     https://download.docker.com/linux/debian bookworm stable" \
     > /etc/apt/sources.list.d/docker.list \
  && apt-get update \
  && apt-get install -y --no-install-recommends docker-ce-cli \
  && rm -rf /var/lib/apt/lists/*

# ─── AI CLI Agents ───
# Claude Code
RUN npm install -g @anthropic-ai/claude-code @playwright/mcp

# OpenAI Codex CLI (if available)
RUN npm install -g @openai/codex 2>/dev/null || echo "Codex CLI not yet public"

# Google Gemini CLI (if available)
RUN pip3 install --no-cache-dir google-genai --break-system-packages 2>/dev/null \
    || echo "Gemini CLI not yet available"

# Rust toolchain
RUN curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs \
  | sh -s -- -y --default-toolchain stable
ENV PATH="/root/.cargo/bin:${PATH}"

# Non-root user
RUN useradd -m -s /bin/bash worker \
  && echo "worker ALL=(ALL) NOPASSWD:ALL" > /etc/sudoers.d/worker \
  && mkdir -p /workspace && chown worker:worker /workspace

USER worker
WORKDIR /workspace

ENV DISPLAY=:99
EXPOSE 7900

COPY entrypoint.sh /entrypoint.sh
USER root
RUN chmod +x /entrypoint.sh
USER worker

ENTRYPOINT ["/entrypoint.sh"]
```

### 9.3 IContainerManager Interface

```csharp
// MagicPAI.Core/Services/IContainerManager.cs

public interface IContainerManager
{
    /// <summary>Create and start a worker container.</summary>
    Task<ContainerInfo> SpawnAsync(ContainerConfig config, CancellationToken ct);

    /// <summary>Execute a command in a running container. Returns exit code + output.</summary>
    Task<ExecResult> ExecAsync(string containerId, string command,
        string workDir, CancellationToken ct);

    /// <summary>Execute with real-time output streaming.</summary>
    Task<ExecResult> ExecStreamingAsync(string containerId, string command,
        Action<string> onOutput, TimeSpan timeout, CancellationToken ct);

    /// <summary>Stop and remove a container.</summary>
    Task DestroyAsync(string containerId, CancellationToken ct);

    /// <summary>Check if container is running.</summary>
    Task<bool> IsRunningAsync(string containerId, CancellationToken ct);

    /// <summary>Get container GUI URL (if noVNC enabled).</summary>
    string? GetGuiUrl(string containerId);
}

public record ContainerInfo(string ContainerId, string? GuiUrl);
public record ExecResult(int ExitCode, string Output, string Error);
```

---

## 10. Phase 3 — AI Agent Activities

### 10.1 Deliverables
- `ICliAgentRunner` interface + `ClaudeRunner`, `CodexRunner`, `GeminiRunner`
- `ICliAgentFactory` for creating runners by name
- `RunCliAgentActivity` (generic agent execution)
- `TriageActivity` (complexity classification)
- `ArchitectActivity` (task decomposition)
- `ModelRouterActivity` (model selection)

### 10.2 ICliAgentRunner Interface

```csharp
// MagicPAI.Core/Services/ICliAgentRunner.cs

public interface ICliAgentRunner
{
    string AgentName { get; }  // "claude", "codex", "gemini"

    /// <summary>Build the CLI command string to execute in a container.</summary>
    string BuildCommand(string prompt, string model, int maxTurns, string workDir);

    /// <summary>Parse the raw output from the CLI agent.</summary>
    CliAgentResponse ParseResponse(string rawOutput);

    /// <summary>Get the default model for this agent.</summary>
    string DefaultModel { get; }

    /// <summary>List available models.</summary>
    string[] AvailableModels { get; }
}

public record CliAgentResponse(
    bool Success,
    string Output,
    decimal CostUsd,
    string[] FilesModified,
    int InputTokens,
    int OutputTokens,
    string? SessionId);
```

### 10.3 ClaudeRunner (Adapted from MagicPrompt)

```csharp
// MagicPAI.Core/Services/ClaudeRunner.cs

public class ClaudeRunner : ICliAgentRunner
{
    public string AgentName => "claude";
    public string DefaultModel => "sonnet";
    public string[] AvailableModels => new[]
        { "haiku", "sonnet", "opus" };

    public string BuildCommand(string prompt, string model,
        int maxTurns, string workDir)
    {
        var escapedPrompt = prompt.Replace("'", "'\\''");
        return $"cd {workDir} && claude " +
               $"--dangerously-skip-permissions " +
               $"-p '{escapedPrompt}' " +
               $"--model claude-{ResolveModel(model)} " +
               $"--max-turns {maxTurns} " +
               $"--output-format stream-json";
    }

    public CliAgentResponse ParseResponse(string rawOutput)
    {
        // Parse stream-json format: each line is a JSON object
        // Last "result" message contains cost data and final output
        var lines = rawOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var lastResult = lines
            .Select(line => TryParseJson(line))
            .Where(j => j != null && j.Value.TryGetProperty("type", out var t)
                        && t.GetString() == "result")
            .LastOrDefault();

        if (lastResult == null)
            return new(false, rawOutput, 0, Array.Empty<string>(), 0, 0, null);

        var r = lastResult.Value;
        return new(
            Success: r.TryGetProperty("is_error", out var e) && !e.GetBoolean(),
            Output: r.TryGetProperty("result", out var res) ? res.GetString() ?? "" : "",
            CostUsd: ExtractCost(r),
            FilesModified: ExtractFiles(r),
            InputTokens: ExtractTokens(r, "input"),
            OutputTokens: ExtractTokens(r, "output"),
            SessionId: r.TryGetProperty("session_id", out var sid)
                ? sid.GetString() : null);
    }

    private string ResolveModel(string alias) => alias switch
    {
        "haiku" => "haiku-4-5-20251001",
        "sonnet" => "sonnet-4-6-20250627",
        "opus" => "opus-4-6-20250627",
        _ => alias
    };

    // ... helper methods for JSON parsing
}
```

### 10.4 Triage Activity

```csharp
[Activity("MagicPAI", "AI Agents", "Classify prompt complexity")]
[FlowNode("Simple", "Complex")]
public class TriageActivity : Activity
{
    [Input(DisplayName = "Prompt", UIHint = InputUIHints.MultiLine)]
    public Input<string> Prompt { get; set; } = default!;

    [Input(DisplayName = "Container ID")]
    public Input<string> ContainerId { get; set; } = default!;

    [Output] public Output<int> Complexity { get; set; } = default!;
    [Output] public Output<string> Category { get; set; } = default!;
    [Output] public Output<string> RecommendedModel { get; set; } = default!;

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext ctx)
    {
        var containerMgr = ctx.GetRequiredService<IContainerManager>();
        var runner = new ClaudeRunner();

        // Use Haiku for cheap triage
        var triagePrompt = BuildTriagePrompt(Prompt.Get(ctx));
        var command = runner.BuildCommand(triagePrompt, "haiku", 1, "/workspace");
        var result = await containerMgr.ExecAsync(
            ContainerId.Get(ctx), command, "/workspace", ctx.CancellationToken);

        var parsed = ParseTriageResponse(result.Output);
        Complexity.Set(ctx, parsed.Complexity);
        Category.Set(ctx, parsed.Category);
        RecommendedModel.Set(ctx, parsed.RecommendedModel);

        var outcome = parsed.Complexity >= 7 ? "Complex" : "Simple";
        await ctx.CompleteActivityWithOutcomesAsync(outcome);
    }

    private string BuildTriagePrompt(string userPrompt) =>
        $"""
        Analyze this coding task and respond with JSON only:
        {{
          "complexity": <1-10>,
          "category": "<code_gen|bug_fix|refactor|architecture|testing|docs>",
          "needs_decomposition": <true|false>,
          "recommended_model": "<haiku|sonnet|opus>",
          "estimated_tasks": <number if decomposition needed>,
          "reasoning": "<brief explanation>"
        }}

        Task: {userPrompt}
        """;
}
```

---

## 11. Phase 4 — Verification Pipeline Activities

### 11.1 Deliverables
- `VerificationPipeline` service (chain of gates)
- Individual gate classes (copied + cleaned from MagicPrompt)
- `RunVerificationActivity` (execute gate chain in container)
- `RepairActivity` (AI-generated fix for failed gates)
- Repair loop = Elsa `While` activity (NOT custom loop code)

### 11.2 Gate Interface (Simplified)

```csharp
// MagicPAI.Core/Services/Gates/IVerificationGate.cs

public interface IVerificationGate
{
    string Name { get; }
    bool IsBlocking { get; }

    /// <summary>Can this gate run for the given project?</summary>
    Task<bool> CanVerifyAsync(IContainerManager container,
        string containerId, string workDir, CancellationToken ct);

    /// <summary>Run the verification. Returns pass/fail + details.</summary>
    Task<GateResult> VerifyAsync(IContainerManager container,
        string containerId, string workDir, CancellationToken ct);
}
```

### 11.3 Verification Pipeline Service

```csharp
// MagicPAI.Core/Services/VerificationPipeline.cs

public class VerificationPipeline
{
    private readonly IEnumerable<IVerificationGate> _gates;

    public VerificationPipeline(IEnumerable<IVerificationGate> gates)
    {
        _gates = gates;
    }

    public async Task<PipelineResult> RunAsync(
        IContainerManager container, string containerId,
        string workDir, string[] gateFilter,
        string? workerOutput, CancellationToken ct)
    {
        var results = new List<GateResult>();
        var gates = _gates
            .Where(g => gateFilter.Contains(g.Name))
            .ToList();

        foreach (var gate in gates)
        {
            if (!await gate.CanVerifyAsync(container, containerId, workDir, ct))
                continue;

            var result = await gate.VerifyAsync(
                container, containerId, workDir, ct);
            results.Add(result);

            // Early stop on blocking gate failure
            if (gate.IsBlocking && !result.Passed)
                break;
        }

        return new PipelineResult
        {
            Gates = results,
            AllPassed = results.All(r => r.Passed || !IsBlocking(r.Name)),
        };
    }

    private bool IsBlocking(string gateName) =>
        _gates.FirstOrDefault(g => g.Name == gateName)?.IsBlocking ?? true;
}
```

### 11.4 Repair Loop (Elsa-Native Pattern)

Instead of a custom `FeedbackLoopController`, the repair loop is a
standard Elsa `While` activity in the workflow:

```csharp
// In FullOrchestrateWorkflow.cs:

var repairAttempt = builder.WithVariable<int>("RepairAttempt", 0);
var maxRepairs = builder.WithVariable<int>("MaxRepairs", 5);
var verificationPassed = builder.WithVariable<bool>("VerificationPassed", false);

// After agent execution:
var repairLoop = new While
{
    Condition = new(ctx =>
        !verificationPassed.Get(ctx)
        && repairAttempt.Get(ctx) < maxRepairs.Get(ctx)),
    Body = new Sequence
    {
        Activities =
        {
            // 1. Generate repair prompt from failed gates
            new RepairActivity
            {
                ContainerId = new(containerId),
                FailedGates = new(ctx => failedGates.Get(ctx)),
                OriginalPrompt = new(ctx => prompt.Get(ctx))
            },

            // 2. Run AI agent with repair prompt
            new RunCliAgentActivity
            {
                ContainerId = new(containerId),
                Agent = new("claude"),
                Prompt = new(ctx => repairPrompt.Get(ctx)),
                Model = new("sonnet")
            },

            // 3. Re-verify
            new RunVerificationActivity
            {
                ContainerId = new(containerId),
                WorkingDirectory = new("/workspace"),
                Gates = new(new[] { "compile", "test", "hallucination" })
            },

            // 4. Update loop variables
            new SetVariable
            {
                Variable = verificationPassed,
                Value = new(ctx => ctx.GetOutput("RunVerification", "AllPassed"))
            },
            new SetVariable
            {
                Variable = repairAttempt,
                Value = new(ctx => repairAttempt.Get(ctx) + 1)
            }
        }
    }
};
```

---

## 12. Phase 5 — Orchestration Workflows

### 12.1 Deliverables
- `FullOrchestrateWorkflow` (Elsa WorkflowBase)
- `SimpleAgentWorkflow` (single agent, no decomposition)
- `VerifyAndRepairWorkflow` (reusable sub-workflow)
- JSON templates for Elsa Studio import

### 12.2 Full Orchestrate Workflow (Elsa-Native)

```csharp
// MagicPAI.Workflows/FullOrchestrateWorkflow.cs

public class FullOrchestrateWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        // ─── Variables ───
        var prompt = builder.WithVariable<string>("Prompt", "");
        var workspacePath = builder.WithVariable<string>("WorkspacePath", "");
        var containerId = builder.WithVariable<string>("ContainerId", "");
        var complexity = builder.WithVariable<int>("Complexity", 0);
        var agentResponse = builder.WithVariable<string>("AgentResponse", "");
        var verificationPassed = builder.WithVariable<bool>("Passed", false);
        var taskList = builder.WithVariable<string[]>("TaskList",
            Array.Empty<string>());

        // ─── Activities ───

        // 1. Spawn worker container
        var spawn = new SpawnContainerActivity
        {
            WorkspacePath = new(workspacePath),
            EnableGui = new(false)
        };

        // 2. Triage: classify complexity
        var triage = new TriageActivity
        {
            Prompt = new(prompt),
            ContainerId = new(containerId)
        };

        // 3a. SIMPLE PATH: single agent execution
        var simpleAgent = new RunCliAgentActivity
        {
            Agent = new("claude"),
            Prompt = new(prompt),
            ContainerId = new(containerId),
            Model = new("sonnet")
        };

        // 3b. COMPLEX PATH: architect decomposes into tasks
        var architect = new ArchitectActivity
        {
            Prompt = new(prompt),
            ContainerId = new(containerId)
        };

        // 4. Parallel task execution via ForEach
        var parallelTasks = new ForEach
        {
            Items = new(taskList),
            Body = new Sequence
            {
                Activities =
                {
                    // Each task gets its own sub-container + worktree
                    new CreateWorktreeActivity
                    {
                        ContainerId = new(containerId),
                        BranchName = new(ctx =>
                            $"task-{ctx.GetVariable<string>("CurrentValue")}")
                    },
                    new RunCliAgentActivity
                    {
                        Agent = new("claude"),
                        ContainerId = new(containerId),
                        Prompt = new(ctx => ctx.GetVariable<string>("CurrentValue")),
                        Model = new("sonnet")
                    },
                    // Verify & Repair sub-workflow
                    new DispatchWorkflow
                    {
                        WorkflowDefinitionId = new(nameof(VerifyAndRepairWorkflow)),
                        WaitForCompletion = new(true),
                        Input = new(ctx => new Dictionary<string, object>
                        {
                            ["ContainerId"] = containerId.Get(ctx),
                            ["WorkDir"] = "/workspace"
                        })
                    },
                    new MergeWorktreeActivity
                    {
                        ContainerId = new(containerId)
                    }
                }
            }
        };

        // 5. Cleanup
        var cleanup = new DestroyContainerActivity
        {
            ContainerId = new(containerId)
        };

        // ─── Flowchart ───
        builder.Root = new Flowchart
        {
            Activities = { spawn, triage, simpleAgent,
                           architect, parallelTasks, cleanup },
            Connections =
            {
                new(spawn, triage),
                new(triage, "Simple", simpleAgent),
                new(triage, "Complex", architect),
                new(architect, parallelTasks),
                new(simpleAgent, cleanup),
                new(parallelTasks, cleanup)
            }
        };
    }
}
```

### 12.3 VerifyAndRepair Sub-Workflow

```csharp
public class VerifyAndRepairWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        var containerId = builder.WithVariable<string>("ContainerId", "");
        var workDir = builder.WithVariable<string>("WorkDir", "/workspace");
        var attempt = builder.WithVariable<int>("Attempt", 0);
        var maxAttempts = builder.WithVariable<int>("MaxAttempts", 5);
        var passed = builder.WithVariable<bool>("Passed", false);
        var failedGates = builder.WithVariable<string[]>("FailedGates",
            Array.Empty<string>());

        var verify = new RunVerificationActivity
        {
            ContainerId = new(containerId),
            WorkingDirectory = new(workDir),
            Gates = new(new[] { "compile", "test", "security", "hallucination" })
        };

        var repairLoop = new While
        {
            Condition = new(ctx =>
                !passed.Get(ctx) && attempt.Get(ctx) < maxAttempts.Get(ctx)),
            Body = new Sequence
            {
                Activities =
                {
                    new RepairActivity
                    {
                        ContainerId = new(containerId),
                        FailedGates = new(failedGates),
                    },
                    new RunCliAgentActivity
                    {
                        Agent = new("claude"),
                        ContainerId = new(containerId),
                        Model = new("sonnet")
                    },
                    verify, // re-verify
                }
            }
        };

        builder.Root = new Sequence
        {
            Activities = { verify, repairLoop }
        };

        // Wire output: Set workflow output
        builder.WithOutput("Passed", ctx => passed.Get(ctx));
    }
}
```

---

## 13. Phase 6 — Frontend (Blazor WASM — Extends Elsa Studio)

### 13.1 Deliverables
- Single Blazor WASM app that extends Elsa Studio with custom pages
- No iframe, no separate SPA — Elsa Studio and MagicPAI pages share one app
- Custom pages: Dashboard, SessionView, CostDashboard, Settings
- Type-safe SignalR client in C# (not JavaScript)
- Custom activity property panels (extend Elsa Studio designer)

### 13.2 Why Blazor (Not React)

| Factor | React (old plan) | Blazor (new plan) |
|---|---|---|
| Elsa Studio integration | iframe (routing conflicts) | Native — same Blazor app |
| Extend Studio designer | Impossible | Add custom panels/pages directly |
| Language | TypeScript + C# (two stacks) | C# everywhere (one stack) |
| SignalR client | JS client (stringly typed) | .NET client (type-safe) |
| Build toolchain | Node + Vite + npm + dotnet | dotnet only |
| Deployment | Two SPAs to serve | One app |
| Share types | Duplicate in types.ts | Share MagicPAI.Core models directly |
| Auth | Separate auth flow | Share Elsa Identity |

### 13.3 Studio Program.cs

```csharp
// MagicPAI.Studio/Program.cs

using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Elsa.Studio.Extensions;
using Elsa.Studio.Core.BlazorWasm;
using MagicPAI.Studio.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// ─── Elsa Studio (built-in workflow designer) ───
builder.Services.AddElsaStudio(studio =>
{
    studio.UseBackendUrl(
        builder.Configuration["Elsa:Server:BaseUrl"] ?? "/elsa/api");
});

// ─── MagicPAI custom services ───
builder.Services.AddScoped<SessionHubClient>();
builder.Services.AddScoped<SessionApiClient>();

// ─── Custom menu items in Elsa Studio sidebar ───
builder.Services.AddElsaStudioMenu(menu =>
{
    menu.AddItem("Dashboard", "/", "home");
    menu.AddItem("Sessions", "/sessions", "play-circle");
    menu.AddItem("Cost Analytics", "/costs", "dollar-sign");
    menu.AddItem("Settings", "/settings", "settings");
});

await builder.Build().RunAsync();
```

### 13.4 Type-Safe SignalR Client (C#)

```csharp
// MagicPAI.Studio/Services/SessionHubClient.cs

using Microsoft.AspNetCore.SignalR.Client;

public class SessionHubClient : IAsyncDisposable
{
    private readonly HubConnection _connection;

    // ─── Type-safe events ───
    public event Action<OutputChunkEvent>? OnOutputChunk;
    public event Action<WorkflowProgressEvent>? OnWorkflowProgress;
    public event Action<VerificationUpdateEvent>? OnVerificationUpdate;
    public event Action<CostUpdateEvent>? OnCostUpdate;
    public event Action<SessionStateEvent>? OnSessionStateChanged;
    public event Action<ContainerEvent>? OnContainerSpawned;
    public event Action<ErrorEvent>? OnError;

    public SessionHubClient(IConfiguration config)
    {
        var hubUrl = config["MagicPAI:HubUrl"] ?? "/hub";
        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect(new[] { TimeSpan.Zero,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(3),
                TimeSpan.FromSeconds(5) })
            .Build();

        // Type-safe event registration (no string event names!)
        _connection.On<OutputChunkEvent>("outputChunk",
            e => OnOutputChunk?.Invoke(e));
        _connection.On<WorkflowProgressEvent>("workflowProgress",
            e => OnWorkflowProgress?.Invoke(e));
        _connection.On<VerificationUpdateEvent>("verificationUpdate",
            e => OnVerificationUpdate?.Invoke(e));
        _connection.On<CostUpdateEvent>("costUpdate",
            e => OnCostUpdate?.Invoke(e));
        _connection.On<SessionStateEvent>("sessionStateChanged",
            e => OnSessionStateChanged?.Invoke(e));
        _connection.On<ContainerEvent>("containerSpawned",
            e => OnContainerSpawned?.Invoke(e));
        _connection.On<ErrorEvent>("error",
            e => OnError?.Invoke(e));
    }

    public async Task ConnectAsync() =>
        await _connection.StartAsync();

    // ─── Type-safe hub method invocations ───
    public async Task<string> CreateSessionAsync(
        string prompt, string workspacePath,
        string agent = "claude", string model = "auto") =>
        await _connection.InvokeAsync<string>("CreateSession",
            prompt, workspacePath, agent, model);

    public async Task StopSessionAsync(string sessionId) =>
        await _connection.InvokeAsync("StopSession", sessionId);

    public async Task ApproveAsync(string sessionId, string decision) =>
        await _connection.InvokeAsync("Approve", sessionId, decision);

    public async ValueTask DisposeAsync() =>
        await _connection.DisposeAsync();
}
```

### 13.5 Example Page: Dashboard.razor

```razor
@page "/"
@using MagicPAI.Studio.Services
@inject SessionHubClient Hub
@inject SessionApiClient Api

<PageTitle>MagicPAI Dashboard</PageTitle>

<div class="dashboard">
    <h2>Sessions</h2>

    <!-- Quick Start -->
    <div class="quick-start">
        <textarea @bind="prompt" placeholder="Describe your task..." rows="3" />
        <div class="controls">
            <select @bind="selectedAgent">
                <option value="claude">Claude Code</option>
                <option value="codex">Codex CLI</option>
                <option value="gemini">Gemini CLI</option>
            </select>
            <input @bind="workspacePath" placeholder="/path/to/project" />
            <button @onclick="StartSession" disabled="@isStarting">
                @(isStarting ? "Starting..." : "Start Session")
            </button>
        </div>
    </div>

    <!-- Session List -->
    <div class="session-list">
        @foreach (var session in sessions)
        {
            <div class="session-card @session.State"
                 @onclick="() => NavigateToSession(session.Id)">
                <span class="status">@session.State</span>
                <span class="id">@session.Id[..8]</span>
                <span class="cost">$@session.TotalCostUsd.ToString("F4")</span>
            </div>
        }
    </div>
</div>

@code {
    private string prompt = "";
    private string selectedAgent = "claude";
    private string workspacePath = "";
    private bool isStarting;
    private List<SessionInfo> sessions = new();

    protected override async Task OnInitializedAsync()
    {
        Hub.OnSessionStateChanged += OnStateChanged;
        await Hub.ConnectAsync();
        sessions = await Api.ListSessionsAsync();
    }

    private async Task StartSession()
    {
        isStarting = true;
        var sessionId = await Hub.CreateSessionAsync(
            prompt, workspacePath, selectedAgent);
        NavigateToSession(sessionId);
        isStarting = false;
    }

    private void OnStateChanged(SessionStateEvent e)
    {
        var session = sessions.FirstOrDefault(s => s.Id == e.SessionId);
        if (session != null) session.State = e.State;
        InvokeAsync(StateHasChanged);
    }

    private void NavigateToSession(string id) =>
        Navigation.NavigateTo($"/sessions/{id}");
}
```

### 13.6 Example Page: SessionView.razor

```razor
@page "/sessions/{SessionId}"
@inject SessionHubClient Hub

<PageTitle>Session @SessionId[..8]</PageTitle>

<div class="session-view">
    <!-- Output Panel (streaming) -->
    <div class="output-panel">
        <pre @ref="outputRef">@output</pre>
    </div>

    <!-- Sidebar: Status + Verification -->
    <div class="sidebar">
        <h3>Status: @state</h3>

        <CostTracker SessionId="@SessionId" />

        @if (verificationUpdate != null)
        {
            <VerificationBadge Update="@verificationUpdate" />
        }

        @if (state == "running")
        {
            <button @onclick="Stop" class="btn-danger">Stop</button>
        }
    </div>
</div>

@code {
    [Parameter] public string SessionId { get; set; } = "";

    private string output = "";
    private string state = "idle";
    private VerificationUpdateEvent? verificationUpdate;
    private ElementReference outputRef;

    protected override async Task OnInitializedAsync()
    {
        Hub.OnOutputChunk += OnChunk;
        Hub.OnSessionStateChanged += OnState;
        Hub.OnVerificationUpdate += OnVerification;
    }

    private void OnChunk(OutputChunkEvent e)
    {
        if (e.SessionId != SessionId) return;
        output += e.Text;
        InvokeAsync(StateHasChanged);
    }

    private void OnState(SessionStateEvent e)
    {
        if (e.SessionId != SessionId) return;
        state = e.State;
        InvokeAsync(StateHasChanged);
    }

    private void OnVerification(VerificationUpdateEvent e)
    {
        if (e.SessionId != SessionId) return;
        verificationUpdate = e;
        InvokeAsync(StateHasChanged);
    }

    private async Task Stop() =>
        await Hub.StopSessionAsync(SessionId);
}
```

### 13.7 SignalR Events (Simplified from MagicPrompt's 70+)

| Event | C# Type | Purpose |
|---|---|---|
| `outputChunk` | `OutputChunkEvent` | Streaming agent output |
| `workflowProgress` | `WorkflowProgressEvent` | Overall progress |
| `verificationUpdate` | `VerificationUpdateEvent` | Gate results |
| `costUpdate` | `CostUpdateEvent` | Cost tracking |
| `sessionStateChanged` | `SessionStateEvent` | Workflow state change |
| `containerSpawned` | `ContainerEvent` | Docker container started |
| `error` | `ErrorEvent` | Error notification |

All event types are C# records shared between server and client via `MagicPAI.Core` —
no more duplicating types in TypeScript.

---

## 14. Phase 7 — Multi-Agent Support

### 14.1 Agent Factory

```csharp
public interface ICliAgentFactory
{
    ICliAgentRunner Create(string agentName);
    string[] AvailableAgents { get; }
}

public class CliAgentFactory : ICliAgentFactory
{
    public string[] AvailableAgents => new[] { "claude", "codex", "gemini" };

    public ICliAgentRunner Create(string agentName) => agentName switch
    {
        "claude" => new ClaudeRunner(),
        "codex" => new CodexRunner(),
        "gemini" => new GeminiRunner(),
        _ => throw new ArgumentException($"Unknown agent: {agentName}")
    };
}
```

### 14.2 CodexRunner

```csharp
public class CodexRunner : ICliAgentRunner
{
    public string AgentName => "codex";
    public string DefaultModel => "o3";
    public string[] AvailableModels => new[] { "o3", "o4-mini", "gpt-4o" };

    public string BuildCommand(string prompt, string model,
        int maxTurns, string workDir)
    {
        var escaped = prompt.Replace("'", "'\\''");
        return $"cd {workDir} && codex " +
               $"--approval-mode full-auto " +
               $"-m {model} " +
               $"'{escaped}'";
    }

    public CliAgentResponse ParseResponse(string rawOutput) =>
        // Parse Codex output format
        new(rawOutput.Contains("error") ? false : true,
            rawOutput, 0, Array.Empty<string>(), 0, 0, null);
}
```

### 14.3 GeminiRunner

```csharp
public class GeminiRunner : ICliAgentRunner
{
    public string AgentName => "gemini";
    public string DefaultModel => "gemini-2.5-pro";
    public string[] AvailableModels =>
        new[] { "gemini-2.5-pro", "gemini-2.5-flash" };

    public string BuildCommand(string prompt, string model,
        int maxTurns, string workDir)
    {
        var escaped = prompt.Replace("'", "'\\''");
        return $"cd {workDir} && gemini " +
               $"--model {model} " +
               $"--sandbox=false " +
               $"'{escaped}'";
    }

    public CliAgentResponse ParseResponse(string rawOutput) =>
        new(true, rawOutput, 0, Array.Empty<string>(), 0, 0, null);
}
```

---

## 15. Phase 8 — Advanced Features

### 15.1 Adaptive Model Routing

```csharp
[Activity("MagicPAI", "AI Agents", "Select the best model based on task and history")]
public class ModelRouterActivity : Activity
{
    [Input] public Input<string> TaskCategory { get; set; } = default!;
    [Input] public Input<int> Complexity { get; set; } = default!;
    [Input] public Input<string> PreferredAgent { get; set; } = new("claude");

    [Output] public Output<string> SelectedAgent { get; set; } = default!;
    [Output] public Output<string> SelectedModel { get; set; } = default!;

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext ctx)
    {
        var complexity = Complexity.Get(ctx);
        var agent = PreferredAgent.Get(ctx);

        // Simple routing rules (can be enhanced with DB history later)
        var model = (agent, complexity) switch
        {
            ("claude", <= 3) => "haiku",
            ("claude", <= 7) => "sonnet",
            ("claude", _) => "opus",
            ("codex", <= 5) => "o4-mini",
            ("codex", _) => "o3",
            ("gemini", _) => "gemini-2.5-pro",
            _ => "sonnet"
        };

        SelectedAgent.Set(ctx, agent);
        SelectedModel.Set(ctx, model);
        await ctx.CompleteActivityAsync();
    }
}
```

### 15.2 SharedBlackboard (File Claims)

```csharp
// Copied from MagicPrompt, simplified

public class SharedBlackboard
{
    private readonly ConcurrentDictionary<string, string> _fileClaims = new();
    private readonly ConcurrentDictionary<string, string> _taskOutputs = new();

    public bool ClaimFile(string filePath, string taskId)
        => _fileClaims.TryAdd(filePath, taskId);

    public bool ReleaseFile(string filePath, string taskId)
        => _fileClaims.TryRemove(filePath, out var owner) && owner == taskId;

    public string? GetFileOwner(string filePath)
        => _fileClaims.TryGetValue(filePath, out var owner) ? owner : null;

    public void SetTaskOutput(string taskId, string output)
        => _taskOutputs[taskId] = output;

    public string? GetTaskOutput(string taskId)
        => _taskOutputs.TryGetValue(taskId, out var output) ? output : null;

    public void Clear() { _fileClaims.Clear(); _taskOutputs.Clear(); }
}
```

### 15.3 Human Approval (Elsa Bookmark)

```csharp
[Activity("MagicPAI", "Control Flow", "Pause for human approval before merge")]
public class HumanApprovalActivity : Activity
{
    [Input(DisplayName = "Message")]
    public Input<string> Message { get; set; } = default!;

    [Input(DisplayName = "Options",
        UIHint = InputUIHints.CheckList,
        Options = new[] { "approve", "reject", "modify" })]
    public Input<string[]> Options { get; set; } =
        new(new[] { "approve", "reject" });

    [Output] public Output<string> Decision { get; set; } = default!;

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext ctx)
    {
        // Create a bookmark — workflow suspends here
        ctx.CreateBookmark(new BookmarkPayload(Message.Get(ctx)),
            OnResumed, includeActivityInstanceId: true);
    }

    private async ValueTask OnResumed(ActivityExecutionContext ctx)
    {
        var input = ctx.GetWorkflowInput<string>("Decision");
        Decision.Set(ctx, input ?? "reject");
        await ctx.CompleteActivityAsync();
    }
}
```

---

## 16. Docker Images

### 16.1 docker-compose.yml

```yaml
version: '3.8'

services:
  server:
    build:
      context: .
      dockerfile: docker/server/Dockerfile
    ports:
      - "5000:8080"        # API + SignalR
      - "5001:8081"        # Elsa Studio
    environment:
      - ConnectionStrings__MagicPai=Host=db;Database=magicpai;Username=magicpai;Password=magicpai
      - MagicPAI__UseDocker=true
      - MagicPAI__WorkerImage=magicpai-env:latest
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock  # Docker-in-Docker
      - workspaces:/workspaces
    depends_on:
      - db

  db:
    image: postgres:17-alpine
    environment:
      POSTGRES_DB: magicpai
      POSTGRES_USER: magicpai
      POSTGRES_PASSWORD: magicpai
    volumes:
      - pgdata:/var/lib/postgresql/data
    ports:
      - "5432:5432"

  worker-env-builder:
    build:
      context: docker/worker-env
      dockerfile: Dockerfile
    image: magicpai-env:latest
    # This service just builds the image, doesn't run
    profiles: ["build"]

volumes:
  pgdata:
  workspaces:
```

### 16.2 Server Dockerfile

```dockerfile
# docker/server/Dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish MagicPAI.Server/MagicPAI.Server.csproj \
    -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

# Docker CLI for managing worker containers
RUN apt-get update && apt-get install -y docker-ce-cli && rm -rf /var/lib/apt/lists/*

EXPOSE 8080 8081
ENTRYPOINT ["dotnet", "MagicPAI.Server.dll"]
```

---

## 17. API & Event Contract

### 17.1 REST API Endpoints

| Method | Path | Purpose |
|---|---|---|
| POST | `/api/sessions` | Create new session (starts workflow) |
| GET | `/api/sessions` | List all sessions |
| GET | `/api/sessions/{id}` | Get session state |
| DELETE | `/api/sessions/{id}` | Stop and delete session |
| POST | `/api/sessions/{id}/approve` | Resume workflow from approval bookmark |
| GET | `/api/sessions/{id}/output` | Get buffered output |
| — | `/elsa/api/*` | Elsa native API (workflow CRUD, instances) |
| — | `/workflows` | Elsa Studio (Blazor WASM) |
| WS | `/hub` | SignalR hub for real-time events |

### 17.2 Session Controller

```csharp
[ApiController]
[Route("api/sessions")]
public class SessionController : ControllerBase
{
    private readonly IWorkflowDispatcher _dispatcher;
    private readonly IWorkflowInstanceStore _instances;

    [HttpPost]
    public async Task<ActionResult<SessionInfo>> Create(
        [FromBody] CreateSessionRequest req)
    {
        var result = await _dispatcher.DispatchAsync(new DispatchWorkflowDefinitionRequest
        {
            DefinitionId = req.WorkflowId ?? "FullOrchestrateWorkflow",
            Input = new Dictionary<string, object>
            {
                ["Prompt"] = req.Prompt,
                ["WorkspacePath"] = req.WorkspacePath,
                ["Agent"] = req.Agent ?? "claude",
                ["Model"] = req.Model ?? "auto"
            }
        });

        return Ok(new SessionInfo
        {
            SessionId = result.InstanceId,
            WorkflowId = req.WorkflowId,
            State = "running"
        });
    }
}
```

---

## 18. Database Schema

### Elsa-Managed Tables (auto-created by EF Core migration)

- `WorkflowDefinitions` — Workflow templates (JSON)
- `WorkflowInstances` — Running/completed workflow instances
- `Bookmarks` — Suspended workflow resumption points
- `ExecutionLogRecords` — Activity execution history
- `WorkflowInboxMessages` — Pending triggers/events

### MagicPAI Custom Tables (optional, for analytics)

```sql
CREATE TABLE execution_records (
    id TEXT PRIMARY KEY,
    session_id TEXT NOT NULL,
    workflow_id TEXT NOT NULL,
    agent TEXT NOT NULL,
    model TEXT NOT NULL,
    prompt_preview TEXT,
    success BOOLEAN,
    cost_usd DECIMAL(10,6),
    input_tokens INTEGER,
    output_tokens INTEGER,
    duration_ms INTEGER,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE gate_results (
    id TEXT PRIMARY KEY,
    session_id TEXT NOT NULL,
    gate_name TEXT NOT NULL,
    passed BOOLEAN,
    output TEXT,
    duration_ms INTEGER,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);
```

---

## 19. Configuration

### 19.1 MagicPaiConfig (Simplified — ~50 properties vs MagicPrompt's 293)

```csharp
public class MagicPaiConfig
{
    // ─── Docker ───
    public bool UseDocker { get; set; } = true;
    public string WorkerImage { get; set; } = "magicpai-env:latest";
    public int DefaultMemoryLimitMb { get; set; } = 4096;
    public int DefaultCpuCount { get; set; } = 2;
    public int MaxConcurrentContainers { get; set; } = 5;
    public int ContainerTimeoutMinutes { get; set; } = 30;

    // ─── AI Agents ───
    public string DefaultAgent { get; set; } = "claude";
    public string DefaultModel { get; set; } = "sonnet";
    public int MaxTurnsPerTask { get; set; } = 20;

    // ─── Verification ───
    public bool EnableVerification { get; set; } = true;
    public bool EnableRepair { get; set; } = true;
    public int MaxRepairAttempts { get; set; } = 5;
    public string[] DefaultGates { get; set; } =
        { "compile", "test", "hallucination" };

    // ─── Budget ───
    public decimal MaxBudgetUsd { get; set; } = 0; // 0 = unlimited
    public bool TrackCosts { get; set; } = true;

    // ─── Git ───
    public bool EnableWorktreeIsolation { get; set; } = true;

    // ─── Triage ───
    public int ComplexityThreshold { get; set; } = 7; // >= threshold = complex
    public string TriageModel { get; set; } = "haiku";

    // ─── GUI ───
    public bool EnableContainerGui { get; set; } = false;
}
```

### 19.2 appsettings.json

```json
{
  "ConnectionStrings": {
    "MagicPai": "Data Source=magicpai.db"
  },
  "MagicPAI": {
    "UseDocker": true,
    "WorkerImage": "magicpai-env:latest",
    "DefaultAgent": "claude",
    "DefaultModel": "sonnet",
    "EnableVerification": true,
    "MaxRepairAttempts": 5,
    "MaxConcurrentContainers": 5,
    "ComplexityThreshold": 7
  },
  "Elsa": {
    "Identity": {
      "Users": [
        { "Name": "admin", "Password": "admin", "Roles": ["admin"] }
      ],
      "Roles": [
        { "Name": "admin", "Permissions": ["*"] }
      ]
    }
  }
}
```

---

## 20. Testing Strategy

### 20.1 Unit Tests

```
MagicPAI.Tests/
├── Services/
│   ├── ClaudeRunnerTests.cs         # CLI command building + response parsing
│   ├── CodexRunnerTests.cs
│   ├── GeminiRunnerTests.cs
│   ├── SharedBlackboardTests.cs     # File claim concurrency
│   ├── VerificationPipelineTests.cs # Gate chain logic
│   └── WorktreeManagerTests.cs      # Git operations
├── Activities/
│   ├── RunCliAgentActivityTests.cs  # Mock container manager
│   ├── TriageActivityTests.cs
│   ├── SpawnContainerActivityTests.cs
│   └── RunVerificationActivityTests.cs
└── Workflows/
    └── FullOrchestrateWorkflowTests.cs  # End-to-end with mocks
```

### 20.2 Integration Tests

- Elsa workflow execution with real activities (in-process, no Docker)
- Docker container lifecycle (spawn → exec → destroy)
- SignalR event delivery (connect, subscribe, receive events)

### 20.3 Mock Container Manager (for tests without Docker)

```csharp
public class MockContainerManager : IContainerManager
{
    public List<string> Commands { get; } = new();

    public Task<ContainerInfo> SpawnAsync(ContainerConfig config, CancellationToken ct)
        => Task.FromResult(new ContainerInfo("mock-container-1", null));

    public Task<ExecResult> ExecAsync(string containerId, string command,
        string workDir, CancellationToken ct)
    {
        Commands.Add(command);
        return Task.FromResult(new ExecResult(0, "mock output", ""));
    }

    public Task DestroyAsync(string containerId, CancellationToken ct)
        => Task.CompletedTask;

    // ...
}
```

---

## 21. Deployment

### 21.1 Development (Local)

```bash
# Build worker image
docker build -t magicpai-env:latest docker/worker-env/

# Run with SQLite (no Docker isolation)
cd MagicPAI.Server
dotnet run -- --MagicPAI:UseDocker=false

# Or with Docker isolation
docker compose up
```

### 21.2 Production (Docker Compose)

```bash
docker compose -f docker-compose.yml up -d
# Server: http://localhost:5000
# Studio: http://localhost:5000/workflows
# SignalR: ws://localhost:5000/hub
```

### 21.3 Production (Kubernetes)

```yaml
# Separate deployments for server, PostgreSQL, and worker image pre-pull
apiVersion: apps/v1
kind: Deployment
metadata:
  name: magicpai-server
spec:
  replicas: 1
  template:
    spec:
      containers:
        - name: server
          image: magicpai-server:latest
          ports:
            - containerPort: 8080
          volumeMounts:
            - name: docker-sock
              mountPath: /var/run/docker.sock
      volumes:
        - name: docker-sock
          hostPath:
            path: /var/run/docker.sock
```

---

## 22. Complete File Manifest

```
MagicPAI/                                    [NEW PROJECT ROOT]
│
├── MagicPAI.sln                             [new]
│
├── MagicPAI.Server/                         [new - ASP.NET Core host]
│   ├── MagicPAI.Server.csproj               [new]
│   ├── Program.cs                           [new - Elsa + SignalR + DI]
│   ├── appsettings.json                     [new - config]
│   ├── Hubs/
│   │   └── SessionHub.cs                    [new - ~200 lines, simplified]
│   ├── Bridge/
│   │   ├── ElsaEventBridge.cs               [new - Elsa → SignalR]
│   │   └── WorkflowProgressTracker.cs       [new]
│   └── Controllers/
│       └── SessionController.cs             [new - REST API]
│
├── MagicPAI.Activities/                     [new - Custom Elsa activities]
│   ├── MagicPAI.Activities.csproj           [new]
│   ├── AI/
│   │   ├── RunCliAgentActivity.cs           [new]
│   │   ├── TriageActivity.cs                [new]
│   │   ├── ArchitectActivity.cs             [new]
│   │   └── ModelRouterActivity.cs           [new]
│   ├── Verification/
│   │   ├── RunVerificationActivity.cs       [new]
│   │   ├── RepairActivity.cs                [new]
│   │   └── Gates/
│   │       ├── CompileGate.cs               [adapted from MP]
│   │       ├── TestGate.cs                  [adapted from MP]
│   │       ├── CoverageGate.cs              [adapted from MP]
│   │       ├── SecurityGate.cs              [adapted from MP]
│   │       ├── LintGate.cs                  [adapted from MP]
│   │       ├── HallucinationDetector.cs     [adapted from MP]
│   │       └── QualityReviewGate.cs         [adapted from MP]
│   ├── Docker/
│   │   ├── SpawnContainerActivity.cs        [new]
│   │   ├── ExecInContainerActivity.cs       [new]
│   │   ├── StreamFromContainerActivity.cs   [new]
│   │   └── DestroyContainerActivity.cs      [new]
│   └── Git/
│       ├── CreateWorktreeActivity.cs        [new]
│       ├── MergeWorktreeActivity.cs         [new]
│       └── CleanupWorktreeActivity.cs       [new]
│
├── MagicPAI.Core/                           [new - Shared library]
│   ├── MagicPAI.Core.csproj                 [new]
│   ├── Models/
│   │   ├── CliAgentResponse.cs              [new]
│   │   ├── VerificationResult.cs            [adapted from MP]
│   │   ├── GateResult.cs                    [adapted from MP]
│   │   ├── TriageResult.cs                  [new, simplified]
│   │   ├── ContainerConfig.cs               [new]
│   │   └── ContainerInfo.cs                 [new]
│   ├── Services/
│   │   ├── ICliAgentRunner.cs               [new interface]
│   │   ├── ICliAgentFactory.cs              [new interface]
│   │   ├── CliAgentFactory.cs               [new]
│   │   ├── ClaudeRunner.cs                  [adapted from MP - cleaned]
│   │   ├── CodexRunner.cs                   [new]
│   │   ├── GeminiRunner.cs                  [new]
│   │   ├── IContainerManager.cs             [new interface]
│   │   ├── DockerContainerManager.cs        [new - Docker.DotNet]
│   │   ├── LocalExecutionEnvironment.cs     [new - no-Docker fallback]
│   │   ├── SharedBlackboard.cs              [copied from MP]
│   │   ├── WorktreeManager.cs               [adapted from MP]
│   │   ├── VerificationPipeline.cs          [adapted from MP]
│   │   └── IVerificationGate.cs             [new, simplified interface]
│   └── Config/
│       └── MagicPaiConfig.cs                [new - ~50 properties]
│
├── MagicPAI.Workflows/                      [new - Built-in templates]
│   ├── MagicPAI.Workflows.csproj            [new]
│   ├── FullOrchestrateWorkflow.cs           [new - Elsa WorkflowBase]
│   ├── SimpleAgentWorkflow.cs               [new]
│   ├── VerifyAndRepairWorkflow.cs           [new]
│   └── Templates/
│       ├── full-orchestrate.json            [new - Elsa JSON]
│       ├── simple-agent.json                [new]
│       └── verify-repair.json               [new]
│
├── MagicPAI.Studio/                         [new - Blazor WASM frontend]
│   ├── MagicPAI.Studio.csproj              [new]
│   ├── Program.cs                           [new - Elsa Studio + custom pages]
│   ├── wwwroot/
│   │   ├── index.html                       [new - Blazor host]
│   │   └── css/app.css                      [new]
│   ├── Layout/
│   │   └── MainLayout.razor                 [new - shared layout]
│   ├── Pages/
│   │   ├── Dashboard.razor                  [new - session list + quick start]
│   │   ├── SessionView.razor               [new - live output + DAG]
│   │   ├── CostDashboard.razor             [new - cost analytics]
│   │   └── Settings.razor                   [new - agent config]
│   ├── Components/
│   │   ├── OutputPanel.razor               [new - streaming output]
│   │   ├── DagView.razor                   [new - DAG progress]
│   │   ├── VerificationBadge.razor         [new - gate pass/fail]
│   │   ├── AgentSelector.razor             [new - agent picker]
│   │   ├── CostTracker.razor              [new - live costs]
│   │   └── ContainerStatus.razor           [new - Docker health]
│   └── Services/
│       ├── SessionHubClient.cs              [new - type-safe SignalR]
│       └── SessionApiClient.cs              [new - REST API client]
│
├── MagicPAI.Tests/                          [new - xUnit]
│   ├── MagicPAI.Tests.csproj               [new]
│   ├── Services/                            [new]
│   ├── Activities/                          [new]
│   └── Workflows/                           [new]
│
└── docker/
    ├── docker-compose.yml                   [new]
    ├── docker-compose.dev.yml               [new]
    ├── server/
    │   └── Dockerfile                       [new]
    └── worker-env/
        ├── Dockerfile                       [adapted from MP env-gui]
        └── entrypoint.sh                    [adapted from MP env-gui]
```

**Total new files: ~65**
**Adapted from MagicPrompt: ~15**
**Copied from MagicPrompt: ~3**

---

## 23. Implementation Timeline

| Phase | Deliverables | Dependencies |
|---|---|---|
| **Phase 1** | Solution structure, Elsa server, Studio, DB, DI | None |
| **Phase 2** | Docker image, ContainerManager, container activities | Phase 1 |
| **Phase 3** | ClaudeRunner, CodexRunner, GeminiRunner, agent activities | Phase 2 |
| **Phase 4** | Verification gates, pipeline, repair activity | Phase 2 |
| **Phase 5** | FullOrchestrateWorkflow, SimpleAgent, VerifyRepair | Phase 3 + 4 |
| **Phase 6** | Blazor WASM frontend (extends Elsa Studio), SignalR bridge | Phase 1 + 5 |
| **Phase 7** | Multi-agent routing, model selection | Phase 3 |
| **Phase 8** | Human approval, SharedBlackboard, cost analytics | Phase 5 |

### Critical Path: Phase 1 → Phase 2 → Phase 3 → Phase 5

### Parallel Work:
- Phase 4 can run alongside Phase 3
- Phase 6 can start after Phase 1 (stub data)
- Phase 7 + 8 are independent enhancements

---

## Appendix A: Why This Approach Over Wrapping MagicPrompt

| Aspect | Wrapping MagicPrompt (old plan) | Fresh MagicPAI (this plan) |
|---|---|---|
| Workflow engine | Custom `WorkflowExecutor` + Elsa wrapper | **Elsa IS the engine** |
| Control flow | 80+ custom node handlers | **Elsa built-in** If/Switch/While/ForEach/Fork |
| State management | Custom DataBus + Elsa variables (dual) | **Elsa variables only** |
| Persistence | Custom SQLite + Elsa EF Core (dual DB) | **Elsa EF Core only** (+ small analytics table) |
| Visual designer | Elsa Studio (same) | **Elsa Studio** (same) |
| Frontend | React + Elsa Studio iframe (two SPAs) | **Blazor WASM** extending Elsa Studio (one app) |
| Code complexity | ~40,000 LOC (MagicPrompt) + wrapper layer | **~5,000 LOC** new code + Elsa |
| Maintenance | Two systems to maintain | **One system** |
| Bugs | Impedance mismatch between MP and Elsa | **Clean interfaces** |
| Docker isolation | Possible but awkward with existing MP code | **First-class design** |
| Multi-agent | Bolt-on to existing ClaudeRunner | **Built-in from day 1** |

---

## Appendix B: Elsa Component Usage Summary

| Elsa Component | How MagicPAI Uses It |
|---|---|
| `Flowchart` | Root activity for all orchestration workflows |
| `Sequence` | Sequential step execution within branches |
| `If` | Triage routing (simple vs complex) |
| `Switch` | Multi-agent routing |
| `While` | Repair loops (verify → fix → re-verify) |
| `ForEach` | Parallel task execution (iterate decomposed tasks) |
| `Fork` + `Join` | Fan-out parallel workers, fan-in results |
| `DispatchWorkflow` | Verify-and-repair sub-workflow |
| `SetVariable` | Update loop counters, store intermediate results |
| `Delay` | Rate limiting between API calls |
| `HttpEndpoint` | Webhook triggers for external integrations |
| Bookmarks | Human approval gates |
| Workflow Variables | All shared state between activities |
| EF Core Persistence | Workflow definitions + instances + execution logs |
| Elsa Studio | Visual workflow designer |
| Identity | Admin authentication for Studio |
| REST API | Workflow CRUD from frontend |
| Activity I/O | Data flow between custom activities |
| Incident Strategies | Error handling and fault recovery |
| Execution Log | Activity-level logging → SignalR bridge |

---

## Appendix C: Docker Isolation — Deep Technical Details

### Elsa Distributed Runtime Options

Elsa 3 provides two runtimes for distributed execution:

1. **DistributedWorkflowRuntime** (default for clusters) — Database/Redis distributed locks
2. **ProtoActorWorkflowRuntime** — Lock-free virtual actors (single-threaded per workflow)

```csharp
// Distributed runtime with Redis locking
elsa.UseWorkflowRuntime(runtime =>
{
    runtime.UseDistributedRuntime();
    runtime.DistributedLockProvider = sp =>
        new RedisDistributedSynchronizationProvider(
            ConnectionMultiplexer.Connect(connStr).GetDatabase());
});
```

### Key Limitation: No Built-in Worker Routing

Elsa does **NOT** have built-in worker tags/routing (like Windmill/Temporal task queues).
To route activities to specific Docker containers, implement a custom `IWorkflowDispatcher`:

```csharp
// Custom dispatcher that routes Docker activities to a RabbitMQ queue
services.AddSingleton<IWorkflowDispatcher, QueueBasedWorkflowDispatcher>();
```

### Docker.DotNet Container Lifecycle

```csharp
// Connect to Docker daemon
var client = new DockerClientConfiguration(
    new Uri("unix:///var/run/docker.sock")).CreateClient();

// Create container with resource limits
var container = await client.Containers.CreateContainerAsync(
    new CreateContainerParameters
    {
        Image = "magicpai-env:latest",
        HostConfig = new HostConfig
        {
            Memory = 4L * 1024 * 1024 * 1024,  // 4GB
            NanoCPUs = 2_000_000_000,           // 2 CPUs
            Binds = new[] { "/projects/foo:/workspace:rw" }
        }
    });

// Execute command in container
var exec = await client.Exec.ExecCreateContainerAsync(container.ID,
    new ContainerExecCreateParameters
    {
        AttachStdout = true, AttachStderr = true,
        Cmd = new[] { "bash", "-c", "cd /workspace && claude -p '...' " }
    });
using var stream = await client.Exec.StartAndAttachContainerExecAsync(exec.ID, false);
```

### Docker Sandboxes (Docker Desktop 4.60+)

Docker's new AI Sandbox feature provides microVM-based isolation for AI agents:

```bash
sbx run claude ~/my-project -- "Add error handling"
```

**Four isolation layers:**
1. **Hypervisor** — Separate kernel per sandbox
2. **Network** — HTTP/HTTPS proxied, raw TCP/UDP blocked
3. **Docker Engine** — Each sandbox gets its own daemon
4. **Credentials** — API keys injected via proxy, never stored in VM

### Recommended Production Architecture

```
Elsa Server (API + Studio + Scheduler)
    │
    ├─ PostgreSQL (state + Quartz + distributed locks)
    ├─ Redis (distributed locking + caching)
    ├─ RabbitMQ (MassTransit dispatch + cache invalidation)
    │
    └─ Worker Node(s) — Elsa instances with UseDistributedRuntime()
         └─ Consume queue → RunInDockerActivity
              └─ Docker.DotNet → ephemeral container
                   ├─ Mount workspace volume
                   ├─ Run AI agent + verification gates
                   ├─ Stream stdout/stderr → Elsa log → SignalR
                   └─ Destroy container on completion
```

---

## Appendix D: Code Reuse Analysis (Detailed)

### COPY Tier (Standalone, No Changes Needed)

| Service | LoC | Dependencies | Notes |
|---|---|---|---|
| `SharedBlackboard.cs` | 245 | None | Pure ConcurrentDictionary, thread-safe file claims |
| `WorktreeManager.cs` | 235 | `ITabExecutionEnvironment` | Pure git commands, swap env interface |
| `CompileGate.cs` | 223 | `ITabExecutionEnvironment` | Detects 11 languages, runs builds |
| `TestGate.cs` | 302 | `ITabExecutionEnvironment` | Detects test frameworks, runs suites |
| `SecurityGate.cs` | 129 | None | Stateless regex scanner |
| `HallucinationDetector.cs` | 211 | None | Static utility, file existence checks |
| `ITabExecutionEnvironment.cs` | 131 | None | Pure interface, 9 methods |

**Total: ~1,476 LoC ready to copy**

### ADAPT Tier (Minor Refactoring)

| Service | LoC | What Changes | Effort |
|---|---|---|---|
| `ClaudeRunner.cs` | 1,337 | Remove CostData coupling, simplify schema gen, remove VCR | Medium |
| `LocalTabEnvironment.cs` | 203 | Extract ClaudeLocator to config | Low |
| `AIRepairPromptGenerator.cs` | 287 | Extract templates, remove PromptVariantStore | Medium |
| `OrchestrationConfig.cs` | 293 | Simplify to ~50 properties, remove MP features | Low |
| `Dockerfile (env-gui)` | 119 | Add Codex/Gemini CLIs, update package versions | Low |

**Total: ~2,239 LoC to adapt**

### REBUILD Tier (Too Coupled, Build Fresh for Elsa)

| Service | LoC | Why Rebuild | Elsa Replacement |
|---|---|---|---|
| `VerificationPipeline.cs` | 403 | App-type routing, visual verification, custom gates | Elsa activity chain |
| `FeedbackLoopController.cs` | 634 | Opus escalation, oracle, session continuity | Elsa While loop + RepairActivity |
| `DockerTabEnvironment.cs` | 1,014 | MCP injection, role labels, health monitoring | DockerContainerManager (Docker.DotNet) |

**Total: ~2,051 LoC to rebuild (but simpler)**

### Summary

- **~1,500 LoC** copy directly
- **~2,200 LoC** adapt (mostly ClaudeRunner cleanup)
- **~2,000 LoC** rebuild fresh (simpler Elsa-native versions)
- **~5,000 LoC** new code (activities, bridge, frontend, config)
- **~40,000 LoC** of MagicPrompt code **NOT needed** (replaced by Elsa)

---

## Appendix E: Elsa Official Docker Images

| Image | Tag | Purpose |
|---|---|---|
| `elsaworkflows/elsa-server-v3-5` | `latest` | API server only |
| `elsaworkflows/elsa-studio-v3-5` | `latest` | Blazor WASM designer |
| `elsaworkflows/elsa-server-and-studio-v3-5` | `latest` | Combined (dev) |

### Production Docker Compose (Clustered)

```yaml
services:
  elsa-node1:
    image: elsaworkflows/elsa-server-v3-5:latest
    environment:
      CONNECTIONSTRINGS__POSTGRESQL: Server=postgres;Database=elsa
      REDIS__CONNECTIONSTRING: redis:6379
      RABBITMQ__CONNECTIONSTRING: amqp://guest:guest@rabbitmq:5672/
      ELSA__RUNTIME__TYPE: Distributed
    ports: ["5001:8080"]

  elsa-node2:
    image: elsaworkflows/elsa-server-v3-5:latest
    # Same env as node1 — auto-clustered via shared DB + Redis
    ports: ["5002:8080"]

  postgres:
    image: postgres:17-alpine
  redis:
    image: redis:7-alpine
  rabbitmq:
    image: rabbitmq:3-management-alpine
```

### Kubernetes Scaling Guidelines

| Component | Replicas | Memory | CPU |
|---|---|---|---|
| Elsa Server | 3-10 (HPA) | 512Mi-2Gi | 500m-2000m |
| Elsa Studio | 2-5 | 256Mi-1Gi | 250m-1000m |
| PostgreSQL | 1 (HA optional) | 2Gi+ | 1000m+ |
| Redis | 1 (Sentinel optional) | 256Mi | 250m |
| RabbitMQ | 1 (cluster optional) | 512Mi | 500m |
