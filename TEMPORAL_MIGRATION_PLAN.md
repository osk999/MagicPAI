# Temporal.io Migration Plan — MagicPAI (Elsa 3.6 → Temporal)

**Branch:** `temporal`
**Date:** 2026-04-20
**Target:** .NET 10, C# 13, Temporalio 1.13.0
**Status:** Plan — no code changes yet

---

## 0. TL;DR

Move MagicPAI's workflow engine from **Elsa 3.6** to **Temporal.io** while keeping everything else intact:
- `MagicPAI.Core` (runners, gates, Docker, auth, blackboard) — **no changes**; it was already engine-agnostic.
- `MagicPAI.Activities` — **rewrite** 32 activity classes as `[Activity]`-annotated methods. Keep the same method surface per activity (inputs/outputs preserved as parameters/returns).
- `MagicPAI.Workflows` (C# + JSON templates) — **rewrite** 26 workflows as `[Workflow]` classes. Delete all 23 JSON templates; Temporal is code-only.
- `MagicPAI.Server` — keep REST/SignalR layer; replace `IWorkflowDispatcher` with `TemporalClient`; replace `ElsaEventBridge` with a Temporal signal+heartbeat side-channel.
- `MagicPAI.Studio` — keep the Blazor WASM custom UX (session dialog, live CLI stream, credentials, containers, gates). **Drop** the Elsa Studio dependency; deep-link to Temporal Web UI at `http://localhost:8233` for execution forensics.
- **Docker stays central.** Every `[Activity]` that runs AI/CLI work continues to execute inside a per-session Docker container via `IContainerManager`. The migration does **not** relax the "always-Docker" rule.

Expected effort: **3 phases, ~2 weeks** of focused work (plan-level estimate, not a commitment):
1. Phase 1 — Temporal infra + one workflow end-to-end (`SimpleAgentWorkflow`)
2. Phase 2 — Port remaining activities and workflows
3. Phase 3 — Retire Elsa, clean up, update docs

---

## 1. Why migrate?

| Problem with Elsa today | How Temporal fixes it |
|---|---|
| Variable-shadowing bug (`GetInput` shadowed by same-named variable) — documented in CLAUDE.md | No shadowing — workflow inputs are method parameters |
| JSON templates cannot express C# delegates → split "C# workflows" vs "JSON workflows" → inconsistent | Code-only workflows; no JSON ↔ C# duality |
| `BulkDispatchWorkflows` / `SetVariable` / `FlowDecision` all require escape hatches | `Workflow.WhenAllAsync` + field assignment + `if/else` — plain C# |
| Bookmarks are brittle (ContainerId propagation bug we just fixed was a bookmark wiring issue) | Child workflow args are type-safe; `Signal` is a typed method call |
| Studio designer is slow, not typed, and has version-drift traps | Temporal UI is forensic-only; workflows are code reviewed in Git |
| Expression languages (JS/C#/Liquid) increase surface area | C# only inside workflows |
| EF Core workflow-instance persistence is opaque to ops | Event history is first-class, queryable, replayable |

Non-goals: Temporal is not a visual designer. We explicitly accept that authoring becomes code-only. We keep our own Blazor UX for session creation and live streaming.

---

## 2. Target architecture

```
┌────────────────────────────────────────────────────────────────────────────┐
│                              Browser (WASM)                                │
│  MagicPAI.Studio — session create, live CLI stream, credentials, gates      │
│     ↓ REST /api/sessions       ↓ SignalR (live streaming)                  │
└────────────────────────────┬─────────────────────────┬─────────────────────┘
                             ↓                         ↓
┌────────────────────────────────────────────────────────────────────────────┐
│                    MagicPAI.Server (ASP.NET Core, .NET 10)                 │
│  • SessionController (REST)                                                │
│  • SessionHub (SignalR) ← ISessionStreamSink ←── Activity stdout           │
│  • SessionHistoryReader → TemporalClient.ListWorkflowExecutions            │
│  • ITemporalClient ──────────► starts workflows, sends signals             │
│  • TemporalWorker (hosted) ──► executes [Workflow] + [Activity]            │
└────────────────────────────────────────────────┬───────────────────────────┘
                                                 │ gRPC :7233
                     ┌───────────────────────────┴───────────┐
                     ↓                                       ↓
           ┌──────────────────┐                   ┌──────────────────┐
           │ Temporal Server  │                   │  Temporal UI     │
           │ (auto-setup img) │                   │  (temporalio/ui) │
           │    :7233 gRPC    │                   │    :8233 HTTP    │
           └────────┬─────────┘                   └──────────────────┘
                    │
                    ↓
           ┌──────────────────┐
           │ PostgreSQL 17    │  (new database: temporal; shared instance)
           └──────────────────┘

Activities (in-process with worker) spawn per-session Docker containers:
      [Activity] → IContainerManager → Docker engine → magicpai-env container
                                         (Claude / Codex / Gemini CLI)
```

Key invariant unchanged: **every activity that runs an AI CLI runs inside a Docker container** (§8).

---

## 3. Project-layout changes

No project renames — just dependency swaps. Changes are file-level.

| Project | Change |
|---|---|
| `MagicPAI.Core` | None (already engine-agnostic). Keep all interfaces/services. |
| `MagicPAI.Activities` | Swap `Elsa.Workflows` NuGet → `Temporalio`. Rewrite every class (§6). |
| `MagicPAI.Workflows` | Swap `Elsa.Workflows` → `Temporalio`. Rewrite every workflow as `[Workflow]` (§7). **Delete** `Templates/*.json`. Delete `WorkflowBase`, `WorkflowBuilderVariableExtensions`, `WorkflowInputHelper`. |
| `MagicPAI.Server` | Remove Elsa packages (`Elsa.*`, `Elsa.EntityFrameworkCore.*`). Add `Temporalio.Extensions.Hosting`. Rewrite `Program.cs`. Delete `Bridge/ElsaEventBridge`, `Bridge/WorkflowPublisher`, `Providers/MagicPaiActivityDescriptorModifier`. Keep `Controllers/`, `Hubs/`, `Bridge/SessionTracker`, `Bridge/WorkflowCatalog` (repurposed). |
| `MagicPAI.Studio` | Remove Elsa Studio packages (`Elsa.Studio.*`). Keep custom pages, SessionApiClient, SessionHubClient. Add a "View in Temporal UI" deep-link page (§9). |
| `MagicPAI.Tests` | Replace mocks of `ActivityExecutionContext` with Temporal test fixtures (`WorkflowEnvironment.StartTimeSkippingAsync`). |

New directory:
- `docker/temporal/` — compose overlay + config for Temporal server + UI.

---

## 4. NuGet package changes

**Add:**
```xml
<!-- MagicPAI.Activities.csproj, MagicPAI.Workflows.csproj -->
<PackageReference Include="Temporalio" Version="1.13.0" />

<!-- MagicPAI.Server.csproj -->
<PackageReference Include="Temporalio" Version="1.13.0" />
<PackageReference Include="Temporalio.Extensions.Hosting" Version="1.13.0" />
<PackageReference Include="Temporalio.Extensions.OpenTelemetry" Version="1.13.0" />

<!-- MagicPAI.Tests.csproj -->
<!-- Testing lives inside Temporalio itself (WorkflowEnvironment) -->
```

**Remove (from all projects that reference them):**
- `Elsa`
- `Elsa.Core`
- `Elsa.Workflows`
- `Elsa.Workflows.Core`
- `Elsa.Workflows.Api`
- `Elsa.Workflows.Management.*`
- `Elsa.Workflows.Runtime.*`
- `Elsa.EntityFrameworkCore`
- `Elsa.EntityFrameworkCore.PostgreSql`
- `Elsa.EntityFrameworkCore.Sqlite`
- `Elsa.Http`
- `Elsa.Expressions.*`
- `Elsa.JavaScript`
- `Elsa.Identity`
- `Elsa.MultiTenancy` (if present)
- `Elsa.Studio.*` (all modules)
- `Elsa.Api.Client`
- `FastEndpoints` (only if it was pulled transitively and unused elsewhere)

---

## 5. Elsa-to-Temporal concept map

| Elsa | Temporal .NET | Notes |
|---|---|---|
| `public class X : Activity` + `ExecuteAsync(ActivityExecutionContext)` | `public async Task<TOut> XAsync(TIn input, CancellationToken ct)` on an activities class with `[Activity]` | Method per activity |
| `Input<T> Prop` + `[Input]` | method parameter | Pass typed record |
| `Output<T> Prop` + `[Output]` | method return value (or record fields) | Single typed return |
| `[FlowNode("Done","Failed")]` outcomes | return value branches + `ApplicationFailureException` for Failed | Workflow picks path in C# |
| `context.GetRequiredService<T>()` | constructor DI on activities class | Register via `AddScopedActivities<MyActivities>()` |
| `context.AddExecutionLogEntry("Event", json)` | `ctx.Logger.LogInformation(...)` + optional heartbeat details | Durable logging via Temporal event history + structured log to sink |
| `context.CreateBookmark(...)` (HumanApproval) | `[WorkflowSignal] ApproveAsync(...)` + `Workflow.WaitConditionAsync(...)` | Signals replace bookmarks |
| `BulkDispatchWorkflows` | `Workflow.WhenAllAsync(items.Select(i => Workflow.StartChildWorkflowAsync(...)))` | Fan-out in C# |
| `SetVariable<T>` with lambda | field assignment on workflow class | No serialization barrier |
| `FlowDecision` with lambda | `if/else` in workflow method | Plain C# |
| `WorkflowBase.Build(builder)` + `Flowchart` | `[Workflow]` class + `[WorkflowRun] RunAsync(...)` | Method body is the workflow |
| `builder.WithVariable<T>("X")` | `private T x;` field | Typed, non-shadowed |
| JSON template (`simple-agent.json`) | C# `[Workflow]` class | Delete JSON |
| `WorkflowCatalog.cs` (registry for Studio) | keep as registry for Blazor Studio's launcher dropdown | Maps to Temporal workflow type + task queue + display metadata |
| Elsa JS expressions in JSON | C# | Remove JS entirely |
| `IWorkflowDispatcher.DispatchAsync` | `ITemporalClient.StartWorkflowAsync` | |
| `IWorkflowCancellationDispatcher.CancelAsync` | `WorkflowHandle.CancelAsync()` or `.TerminateAsync()` | Cancel = cooperative; Terminate = forceful |
| `INotificationHandler<ActivityExecutionLogUpdated>` | Push from activity → `ISessionStreamSink` (SignalR) | Side-channel, not history-channel |

---

## 6. Activity migration (32 activities)

### 6.1 Migration pattern (apply to every activity)

**Before (Elsa):**
```csharp
[Activity("MagicPAI", "AI Agents", "Run a prompt")]
[FlowNode("Done", "Failed")]
public class RunCliAgentActivity : Activity
{
    [Input] public Input<string> Prompt { get; set; } = new();
    [Input] public Input<string> ContainerId { get; set; } = new();
    [Output] public Output<string> Response { get; set; } = new();
    [Output] public Output<bool> Success { get; set; } = new();

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext ctx)
    {
        var factory = ctx.GetRequiredService<ICliAgentFactory>();
        var prompt  = Prompt.Get(ctx);
        var cid     = ContainerId.Get(ctx);
        var runner  = factory.Create("claude");

        var plan = runner.BuildExecutionPlan(prompt, ...);
        var docker = ctx.GetRequiredService<IContainerManager>();
        var result = await docker.ExecStreamingAsync(cid, plan.Command, ctx.CancellationToken)
            .ToListAsync();

        Response.Set(ctx, string.Join("\n", result));
        Success.Set(ctx, true);
        await ctx.CompleteActivityWithOutcomesAsync("Done");
    }
}
```

**After (Temporal):**
```csharp
// Typed I/O records live next to the activity class.
public record RunCliAgentInput(
    string Prompt,
    string ContainerId,
    string AiAssistant = "claude",
    string? Model = null,
    string ModelPower = "standard",
    string? StructuredOutputSchema = null,
    int MaxTurns = 0,
    int TimeoutMinutes = 120,
    string? SessionId = null);       // for SignalR side-channel

public record RunCliAgentOutput(
    string Response,
    string? StructuredOutputJson,
    bool Success,
    decimal CostUsd,
    IReadOnlyList<string> FilesModified,
    int ExitCode);

public class AiActivities
{
    private readonly ICliAgentFactory _factory;
    private readonly IContainerManager _docker;
    private readonly ISessionStreamSink _sink;       // SignalR-side streaming
    private readonly AuthRecoveryService _auth;
    private readonly CredentialInjector _creds;
    private readonly ILogger<AiActivities> _log;

    public AiActivities(ICliAgentFactory factory, IContainerManager docker,
        ISessionStreamSink sink, AuthRecoveryService auth,
        CredentialInjector creds, ILogger<AiActivities> log)
    { _factory = factory; _docker = docker; _sink = sink;
      _auth = auth; _creds = creds; _log = log; }

    [Activity]
    public async Task<RunCliAgentOutput> RunCliAgentAsync(RunCliAgentInput input)
    {
        var ctx = ActivityExecutionContext.Current;
        var ct  = ctx.CancellationToken;

        // Resume marker: on retry, skip output we already streamed
        var resumeOffset = ctx.Info.HeartbeatDetails.Count > 0
            ? await ctx.Info.HeartbeatDetailAtAsync<int>(0) : 0;

        var runner = _factory.Create(input.AiAssistant);
        var plan   = runner.BuildExecutionPlan(input.Prompt, input.Model,
                                               input.ModelPower, input.StructuredOutputSchema);

        var lineCount = 0;
        var captured = new StringBuilder();
        try
        {
            await foreach (var line in _docker.ExecStreamingAsync(input.ContainerId, plan.Command, ct))
            {
                lineCount++;
                if (lineCount <= resumeOffset) continue;     // replayed, skip side-channel
                captured.AppendLine(line);
                if (input.SessionId is not null)
                    await _sink.EmitAsync(input.SessionId, line, ct);

                if (lineCount % 20 == 0) ctx.Heartbeat(lineCount);
            }

            var parsed = runner.ParseResponse(captured.ToString());
            return new RunCliAgentOutput(parsed.Response, parsed.StructuredOutputJson,
                parsed.Success, parsed.CostUsd, parsed.FilesModified, parsed.ExitCode);
        }
        catch (OperationCanceledException)
        {
            _log.LogInformation("RunCliAgent cancelled — container {Id}", input.ContainerId);
            throw;
        }
        catch (AuthExpiredException)
        {
            await _auth.RefreshAsync(ct);
            await _creds.InjectAsync(input.ContainerId, ct);
            throw new ApplicationFailureException("Auth refreshed, retry",
                type: "AuthRefreshed", nonRetryable: false);
        }
    }
}
```

Key transforms:
1. `Input<T>` / `Output<T>` → `record` input + `record` output (one per activity).
2. `ctx.GetRequiredService<T>()` → constructor injection.
3. `[FlowNode("Done","Failed")]` → return success path or throw `ApplicationFailureException`. The calling workflow's `try/catch` picks the branch.
4. `ctx.AddExecutionLogEntry("Event", json)` → `_sink.EmitAsync(sessionId, line, ct)` for streaming; `ctx.Logger.LogInformation(...)` for durable log points.
5. Heartbeating every ~20 lines (or 5 seconds) for all long-running activities so cancellation propagates and retries can resume cleanly.
6. Cancellation token flows through every container call.

### 6.2 Full activity inventory & target shapes

For each activity from the audit, the new `[Activity]` method shape:

| # | Current class | New method | Input record | Output record | Runs in Docker |
|---|---|---|---|---|---|
| 1 | `RunCliAgentActivity` | `AiActivities.RunCliAgentAsync` | `RunCliAgentInput` | `RunCliAgentOutput` | Yes |
| 2 | `AiAssistantActivity` | **Delete** (alias of #1) | — | — | — |
| 3 | `TriageActivity` | `AiActivities.TriageAsync` | `TriageInput` | `TriageOutput` | Yes |
| 4 | `ClassifierActivity` | `AiActivities.ClassifyAsync` | `ClassifierInput` | `ClassifierOutput` | Yes |
| 5 | `ModelRouterActivity` | `AiActivities.RouteModelAsync` | `RouteModelInput` | `RouteModelOutput` | No (pure CPU) |
| 6 | `PromptEnhancementActivity` | `AiActivities.EnhancePromptAsync` | `EnhancePromptInput` | `EnhancePromptOutput` | Yes |
| 7 | `ArchitectActivity` | `AiActivities.ArchitectAsync` | `ArchitectInput` | `ArchitectOutput` | Yes |
| 8 | `ResearchPromptActivity` | `AiActivities.ResearchPromptAsync` | `ResearchPromptInput` | `ResearchPromptOutput` | Yes |
| 9 | `WebsiteTaskClassifierActivity` | `AiActivities.ClassifyWebsiteTaskAsync` | `WebsiteClassifyInput` | `WebsiteClassifyOutput` | Yes |
| 10 | `RequirementsCoverageActivity` | `AiActivities.GradeCoverageAsync` | `CoverageInput` | `CoverageOutput` | Yes |
| 11 | `SpawnContainerActivity` | `DockerActivities.SpawnAsync` | `SpawnContainerInput` | `SpawnContainerOutput` | Yes (spawns) |
| 12 | `ExecInContainerActivity` | `DockerActivities.ExecAsync` | `ExecInput` | `ExecOutput` | Yes |
| 13 | `StreamFromContainerActivity` | `DockerActivities.StreamAsync` | `StreamInput` | `StreamOutput` | Yes |
| 14 | `DestroyContainerActivity` | `DockerActivities.DestroyAsync` | `DestroyInput` | `Unit` | Yes |
| 15 | `CreateWorktreeActivity` | `GitActivities.CreateWorktreeAsync` | `WorktreeInput` | `WorktreeOutput` | Yes |
| 16 | `MergeWorktreeActivity` | `GitActivities.MergeWorktreeAsync` | `WorktreeInput` | `WorktreeMergeOutput` | Yes |
| 17 | `CleanupWorktreeActivity` | `GitActivities.CleanupWorktreeAsync` | `WorktreeInput` | `Unit` | Yes |
| 18 | `RunVerificationActivity` | `VerifyActivities.RunGatesAsync` | `VerifyInput` | `VerifyOutput` | Yes |
| 19 | `RepairActivity` | `VerifyActivities.GenerateRepairPromptAsync` | `RepairInput` | `RepairOutput` | No |
| 20 | `IterationGateActivity` | **Delete** — pure counter, inline as `counter++` in workflow | — | — | — |
| 21 | `HumanApprovalActivity` | **Delete** — replaced by `[WorkflowSignal] ApproveAsync` on the workflow | — | — | — |
| 22 | `ClaimFileActivity` | `BlackboardActivities.ClaimFileAsync` | `ClaimInput` | `ClaimOutput` | No |
| 23 | `UpdateCostActivity` | **Delete** — inline field update in workflow; publish via `ISessionStreamSink` | — | — | — |
| 24 | `EmitOutputChunkActivity` | **Delete** — activities emit directly via `ISessionStreamSink`; workflow never touches it | — | — | — |

Net result: **32 activities → 19 `[Activity]` methods + 5 deletions-as-inlined.**

### 6.3 Class layout

Group activities by category into one class each (DI-friendly, keeps wiring simple):

- `MagicPAI.Activities/AI/AiActivities.cs` — 8 methods
- `MagicPAI.Activities/Docker/DockerActivities.cs` — 4 methods
- `MagicPAI.Activities/Git/GitActivities.cs` — 3 methods
- `MagicPAI.Activities/Verification/VerifyActivities.cs` — 2 methods
- `MagicPAI.Activities/Infrastructure/BlackboardActivities.cs` — 1 method (ClaimFile) + 1 TaskStart if we surface blackboard reads

Plus shared input/output records in `MagicPAI.Activities/Contracts/*.cs`.

---

## 7. Workflow migration (26 C# + 23 JSON)

### 7.1 General rule
Every workflow — whether previously C# or JSON — becomes one `[Workflow]` class. The `WorkflowBase` + flowchart builder goes away.

**Before (Elsa C#):**
```csharp
public class SimpleAgentWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder b)
    {
        b.Name = "Simple Agent";
        var prompt = b.WithVariable<string>("Prompt", "");
        var cid    = b.WithVariable<string>("ContainerId", "");

        var spawn = new SpawnContainerActivity { Id = "spawn" };
        var run   = new RunCliAgentActivity {
            Id = "run",
            Prompt = new Input<string>(ctx => prompt.Get(ctx)),
            ContainerId = new Input<string>(ctx => spawn.ContainerId.Get(ctx))
        };
        var destroy = new DestroyContainerActivity {
            Id = "destroy",
            ContainerId = new Input<string>(ctx => spawn.ContainerId.Get(ctx))
        };

        var fc = new Flowchart { Start = spawn };
        fc.Activities.AddRange(new IActivity[] { spawn, run, destroy });
        fc.Connections.Add(new Connection(spawn, run,   "Done", "In"));
        fc.Connections.Add(new Connection(run,   destroy, "Done", "In"));
        b.Root = fc.WithAttachedVariables(b);
    }
}
```

**After (Temporal):**
```csharp
[Workflow]
public class SimpleAgentWorkflow
{
    private static readonly ActivityOptions ShortAct = new()
    {
        StartToCloseTimeout = TimeSpan.FromMinutes(5),
        HeartbeatTimeout    = TimeSpan.FromSeconds(30),
    };
    private static readonly ActivityOptions LongAct = new()
    {
        StartToCloseTimeout = TimeSpan.FromHours(2),
        HeartbeatTimeout    = TimeSpan.FromSeconds(60),
        CancellationType    = ActivityCancellationType.WaitCancellationCompleted,
        RetryPolicy = new RetryPolicy {
            MaximumAttempts = 3,
            NonRetryableErrorTypes = new[] { "AuthError", "InvalidPrompt" }
        },
    };

    [WorkflowRun]
    public async Task<SimpleAgentOutput> RunAsync(SimpleAgentInput input)
    {
        var spawn = await Workflow.ExecuteActivityAsync(
            (DockerActivities a) => a.SpawnAsync(new SpawnContainerInput(
                Image: "magicpai-env:latest",
                WorkspacePath: input.WorkspacePath,
                SessionId: input.SessionId)),
            ShortAct);

        try
        {
            var run = await Workflow.ExecuteActivityAsync(
                (AiActivities a) => a.RunCliAgentAsync(new RunCliAgentInput(
                    Prompt: input.Prompt,
                    ContainerId: spawn.ContainerId,
                    AiAssistant: input.AiAssistant,
                    Model: input.Model,
                    ModelPower: input.ModelPower,
                    SessionId: input.SessionId)),
                LongAct);

            var verify = await Workflow.ExecuteActivityAsync(
                (VerifyActivities a) => a.RunGatesAsync(new VerifyInput(
                    ContainerId: spawn.ContainerId,
                    WorkingDirectory: input.WorkspacePath,
                    Gates: input.Gates,
                    WorkerOutput: run.Response)),
                ShortAct);

            // Requirements coverage loop (max 3 iterations)
            for (var i = 0; i < 3 && !verify.AllPassed; i++)
            {
                var repair = await Workflow.ExecuteActivityAsync(
                    (AiActivities a) => a.GradeCoverageAsync(new CoverageInput(
                        OriginalPrompt: input.Prompt,
                        ContainerId: spawn.ContainerId,
                        WorkingDirectory: input.WorkspacePath,
                        MaxIterations: 3)),
                    ShortAct);

                if (repair.AllMet) break;

                await Workflow.ExecuteActivityAsync(
                    (AiActivities a) => a.RunCliAgentAsync(new RunCliAgentInput(
                        Prompt: repair.GapPrompt,
                        ContainerId: spawn.ContainerId,
                        AiAssistant: input.AiAssistant,
                        SessionId: input.SessionId)),
                    LongAct);

                verify = await Workflow.ExecuteActivityAsync(
                    (VerifyActivities a) => a.RunGatesAsync(new VerifyInput(
                        ContainerId: spawn.ContainerId,
                        WorkingDirectory: input.WorkspacePath,
                        Gates: input.Gates)),
                    ShortAct);
            }

            return new SimpleAgentOutput(run.Response, verify.AllPassed, run.CostUsd);
        }
        finally
        {
            await Workflow.ExecuteActivityAsync(
                (DockerActivities a) => a.DestroyAsync(new DestroyInput(spawn.ContainerId)),
                new ActivityOptions { StartToCloseTimeout = TimeSpan.FromMinutes(2) });
        }
    }
}
```

**Observations:**
- 40+ lines of Elsa DSL ceremony collapse into ~50 lines of straightforward C#.
- The `ContainerId` propagation bug is structurally impossible — it's a variable in a method.
- Requirements-coverage loop is a plain `for`, not a flowchart cycle.
- The `finally` guarantees container cleanup even if an activity throws (Elsa needed explicit cleanup nodes).

### 7.2 Full workflow inventory

| # | Current workflow | Keep? | Target `[Workflow]` class | Notes |
|---|---|---|---|---|
| 1 | `SimpleAgentWorkflow` | Yes | `SimpleAgentWorkflow` | Per §7.1 above |
| 2 | `VerifyAndRepairWorkflow` | Yes | `VerifyAndRepairWorkflow` | Keep as reusable child workflow for repair loop |
| 3 | `PromptEnhancerWorkflow` | Yes | `PromptEnhancerWorkflow` | Thin wrapper calling `EnhancePromptAsync` |
| 4 | `ContextGathererWorkflow` | Yes | `ContextGathererWorkflow` | Calls research + context activities |
| 5 | `PromptGroundingWorkflow` | Yes | `PromptGroundingWorkflow` | |
| 6 | `IsComplexAppWorkflow` | **Merge** | delete; inline call to `ClassifyAsync` in orchestrators | Single activity wrapping is overhead |
| 7 | `IsWebsiteProjectWorkflow` | **Merge** | delete; inline `ClassifyWebsiteTaskAsync` | Same |
| 8 | `OrchestrateComplexPathWorkflow` | Yes | `OrchestrateComplexPathWorkflow` | Fan-out via `WhenAllAsync` |
| 9 | `ComplexTaskWorkerWorkflow` | Yes | `ComplexTaskWorkerWorkflow` | Child of #8 |
| 10 | `OrchestrateSimplePathWorkflow` | Yes | `OrchestrateSimplePathWorkflow` | |
| 11 | `PostExecutionPipelineWorkflow` | Yes | `PostExecutionPipelineWorkflow` | |
| 12 | `ResearchPipelineWorkflow` | Yes | `ResearchPipelineWorkflow` | |
| 13 | `StandardOrchestrateWorkflow` | Yes | `StandardOrchestrateWorkflow` | |
| 14 | `TestSetPromptWorkflow` | **Delete** | — | Test scaffold, not real workflow |
| 15 | `ClawEvalAgentWorkflow` | Yes | `ClawEvalAgentWorkflow` | |
| 16 | `LoopVerifierWorkflow` | **Delete** | inline loop in `SimpleAgentWorkflow`/`FullOrchestrate` | Elsa had to model this as a workflow; Temporal just loops in C# |
| 17 | `TestClassifierWorkflow` | **Delete** | — | Test scaffold |
| 18 | `TestWebsiteClassifierWorkflow` | **Delete** | — | Test scaffold |
| 19 | `TestPromptEnhancementWorkflow` | **Delete** | — | Test scaffold |
| 20 | `TestFullFlowWorkflow` | **Delete** | — | Test scaffold |
| 21 | `WebsiteAuditCoreWorkflow` | Yes | `WebsiteAuditCoreWorkflow` | |
| 22 | `WebsiteAuditLoopWorkflow` | Yes | `WebsiteAuditLoopWorkflow` | |
| 23 | `FullOrchestrateWorkflow` | Yes | `FullOrchestrateWorkflow` | Central orchestrator; triage → simple/complex path |
| 24 | `DeepResearchOrchestrateWorkflow` | Yes | `DeepResearchOrchestrateWorkflow` | Research-first variant of #23 |

Net: **24 workflows → 15 Temporal workflows + 9 deletions.** Test workflows move to real xUnit tests against `WorkflowEnvironment`.

### 7.3 Signals for human-in-the-loop

Replace `HumanApprovalActivity` (Elsa bookmark) with a typed signal on the workflow that needs approval:

```csharp
[Workflow]
public class OrchestrateWithGateWorkflow
{
    private bool _gateApproved;
    private string? _gateComment;

    [WorkflowSignal]
    public async Task ApproveGateAsync(ApproveGateInput input)
    {
        _gateApproved = true;
        _gateComment = input.Comment;
    }

    [WorkflowSignal]
    public async Task RejectGateAsync(string reason)
    {
        _gateApproved = false;
        _gateComment = reason;
        throw new ApplicationFailureException("Gate rejected", type: "GateRejected");
    }

    [WorkflowRun]
    public async Task RunAsync(...)
    {
        // ... do work ...
        await Workflow.WaitConditionAsync(() => _gateApproved);
        // ... continue ...
    }
}
```

`SessionHub.ApproveGate(sessionId, ...)` will become `client.GetWorkflowHandle(sessionId).SignalAsync(wf => wf.ApproveGateAsync(...))`.

### 7.4 Bulk dispatch (Orchestrate Complex Path)

**Elsa today:** `BulkDispatchWorkflows` activity.
**Temporal:** native parallel child workflows.

```csharp
[WorkflowRun]
public async Task<OrchestrateComplexOutput> RunAsync(OrchestrateComplexInput input)
{
    var triage = await Workflow.ExecuteActivityAsync(
        (AiActivities a) => a.TriageAsync(new TriageInput(input.Prompt, input.ContainerId)),
        ShortAct);

    var plan = await Workflow.ExecuteActivityAsync(
        (AiActivities a) => a.ArchitectAsync(new ArchitectInput(input.Prompt, input.ContainerId)),
        ShortAct);

    var tasks = plan.Tasks.Select(t =>
        Workflow.StartChildWorkflowAsync(
            (ComplexTaskWorkerWorkflow w) => w.RunAsync(new WorkerTaskInput(
                Prompt: t.Description,
                ContainerId: input.ContainerId,
                SessionId: input.SessionId,
                TaskId: t.Id)),
            new ChildWorkflowOptions {
                Id = $"{input.SessionId}-task-{t.Id}",
                ParentClosePolicy = ParentClosePolicy.Terminate
            })).ToList();

    var handles = await Workflow.WhenAllAsync(tasks);
    var results = await Workflow.WhenAllAsync(handles.Select(h => h.GetResultAsync()));

    return new OrchestrateComplexOutput(results.ToList());
}
```

No `BulkDispatch`, no blackboard fan-out tracking, no custom aggregation activity. Just C#.

---

## 8. Docker enforcement strategy

**Requirement (from memory):** every AI/CLI workflow must execute inside Docker — no local mode.

### 8.1 Invariants preserved from Elsa implementation
- `IContainerManager` + `DockerContainerManager` — unchanged. Still uses `Docker.DotNet`.
- `ISessionContainerRegistry`, `IGuiPortAllocator`, `ISessionContainerLogStreamer` — unchanged.
- `docker/worker-env/` image (`magicpai-env:latest`) — unchanged. Claude/Codex/Gemini CLIs + credential mounts.
- Credential mounting (`~/.claude.json`, `~/.credentials.json` → `/tmp/magicpai-host-*` → container `$HOME/.claude/`) — unchanged.
- Auth recovery path (`AuthRecoveryService`, `AuthErrorDetector`, `CredentialInjector`) — unchanged.

### 8.2 Architectural choice: where does Docker live?

**Decision: Docker execution stays inside Temporal *activities*, not workflows.**

Why:
- Workflows must be deterministic. They cannot call Docker directly (no IO in workflow body).
- Activities are the correct boundary for "side-effecting work that takes minutes-to-hours."
- One workflow run ⇒ one logical session ⇒ spawn one container ⇒ run N activity invocations against that same container (reusing via ContainerId passed as activity input).

Every orchestrator workflow follows this shape:
```csharp
var spawn = await SpawnAsync(...);            // 1 container per session
try { /* all CLI activities use spawn.ContainerId */ }
finally { await DestroyAsync(spawn.ContainerId); }
```

### 8.3 Runtime-enforced Docker requirement

To prevent accidental "local mode" regressions, add a startup check:
```csharp
// MagicPAI.Server/Program.cs
builder.Services.AddSingleton<IStartupValidator>(sp =>
{
    var cfg = sp.GetRequiredService<IOptions<MagicPaiConfig>>().Value;
    if (!cfg.UseDocker)
        throw new InvalidOperationException(
            "MagicPAI__UseDocker must be true. Local mode is not supported.");
    return new NoOpStartupValidator();
});
```

And in `DockerActivities.SpawnAsync` itself:
```csharp
if (_env.Kind != ExecutionEnvironmentKind.Docker)
    throw new ApplicationFailureException(
        "Docker is required. Check MagicPAI__UseDocker.", type: "ConfigError", nonRetryable: true);
```

### 8.4 Long-running activity pattern

All activities that `ExecStreamingAsync` inside Docker use this template (from §6.1):
- `StartToCloseTimeout` ≥ longest plausible run (2 h default for RunCliAgent)
- `HeartbeatTimeout` 30–60 s
- `ctx.Heartbeat(offset)` every ~20 lines or 5 s
- `ActivityCancellationType.WaitCancellationCompleted` so workflow cancel waits for container teardown
- Catch `OperationCanceledException` → kill container → rethrow
- Retry policy: 3 attempts, exponential; `NonRetryableErrorTypes = { "AuthError", "InvalidPrompt", "ConfigError" }`

### 8.5 Output streaming — DO NOT route through Temporal history

Claude stdout is 10k–1M tokens per run. Routing it through activity return values or signals would blow the 51 200-event / 50 MB history limit within one session.

**Pattern:** activity writes directly to `ISessionStreamSink` (backed by SignalR hub), which delivers to the Blazor browser. Temporal only stores:
- Activity start/complete events
- Exit code + line count + small structured summary (`RunCliAgentOutput`)
- Heartbeat details: a small resume offset (line number)

This matches what MagicPAI already does via `EmitOutputChunkActivity` → `ElsaEventBridge` → `SessionHub`. We keep the SignalR hub; we only replace the producer side.

---

## 9. Server & Studio integration

### 9.1 Program.cs (MagicPAI.Server) — before/after sketch

**Remove:** entire `builder.AddElsa(...)` block, all Elsa middleware (`app.UseWorkflowsApi("elsa/api")`), `ElsaEventBridge`, `WorkflowPublisher`, `ElsaStudioApiKeyHandler`, `MagicPaiActivityDescriptorModifier`.

**Add:**
```csharp
// Temporal client + worker
builder.Services
    .AddTemporalClient(opts => {
        opts.TargetHost = builder.Configuration["Temporal:Host"] ?? "localhost:7233";
        opts.Namespace  = builder.Configuration["Temporal:Namespace"] ?? "magicpai";
    })
    .AddHostedTemporalWorker(taskQueue: "magicpai-main")
    .AddScopedActivities<AiActivities>()
    .AddScopedActivities<DockerActivities>()
    .AddScopedActivities<GitActivities>()
    .AddScopedActivities<VerifyActivities>()
    .AddScopedActivities<BlackboardActivities>()
    .AddWorkflow<SimpleAgentWorkflow>()
    .AddWorkflow<VerifyAndRepairWorkflow>()
    .AddWorkflow<PromptEnhancerWorkflow>()
    .AddWorkflow<ContextGathererWorkflow>()
    .AddWorkflow<PromptGroundingWorkflow>()
    .AddWorkflow<OrchestrateComplexPathWorkflow>()
    .AddWorkflow<OrchestrateSimplePathWorkflow>()
    .AddWorkflow<ComplexTaskWorkerWorkflow>()
    .AddWorkflow<PostExecutionPipelineWorkflow>()
    .AddWorkflow<ResearchPipelineWorkflow>()
    .AddWorkflow<StandardOrchestrateWorkflow>()
    .AddWorkflow<ClawEvalAgentWorkflow>()
    .AddWorkflow<WebsiteAuditCoreWorkflow>()
    .AddWorkflow<WebsiteAuditLoopWorkflow>()
    .AddWorkflow<FullOrchestrateWorkflow>()
    .AddWorkflow<DeepResearchOrchestrateWorkflow>();

// Core services (unchanged)
builder.Services.AddSingleton<IContainerManager, DockerContainerManager>();
builder.Services.AddSingleton<ICliAgentFactory, CliAgentFactory>();
// ... rest of core wiring unchanged ...

// SessionStreamSink (SignalR-backed)
builder.Services.AddSingleton<ISessionStreamSink, SignalRSessionStreamSink>();
```

### 9.2 SessionController rewrite

`POST /api/sessions` now starts a Temporal workflow:
```csharp
public class SessionController(ITemporalClient temporal, SessionLaunchPlanner planner) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(CreateSessionRequest req, CancellationToken ct)
    {
        var plan = planner.Plan(req);              // selects workflow + input shape
        var workflowId = $"mpai-{Guid.NewGuid():N}";

        // Dispatch based on plan.WorkflowType — switch on type for type-safety
        var handle = plan.WorkflowType switch
        {
            "SimpleAgent"        => await temporal.StartWorkflowAsync<SimpleAgentWorkflow>(
                wf => wf.RunAsync(plan.AsSimpleAgentInput()),
                new WorkflowOptions(workflowId, "magicpai-main")),
            "FullOrchestrate"    => await temporal.StartWorkflowAsync<FullOrchestrateWorkflow>(
                wf => wf.RunAsync(plan.AsFullOrchestrateInput()),
                new WorkflowOptions(workflowId, "magicpai-main")),
            // ... switch arm per supported workflow ...
            _ => throw new ArgumentException($"Unknown workflow: {plan.WorkflowType}")
        };

        return Ok(new { SessionId = workflowId });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Cancel(string id)
    {
        await temporal.GetWorkflowHandle(id).CancelAsync();
        return NoContent();
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id)
    {
        var handle = temporal.GetWorkflowHandle(id);
        var desc = await handle.DescribeAsync();
        return Ok(new {
            Status = desc.Status,
            StartTime = desc.StartTime,
            CloseTime = desc.CloseTime,
            PendingActivities = desc.PendingActivities?.Count ?? 0
        });
    }
}
```

### 9.3 SessionHub

`SessionHub.ApproveGate` becomes:
```csharp
public async Task ApproveGate(string sessionId, ApproveGateInput input)
{
    var handle = _temporal.GetWorkflowHandle(sessionId);
    await handle.SignalAsync<OrchestrateWithGateWorkflow>(wf => wf.ApproveGateAsync(input));
}
```

Output-chunk streaming doesn't change at all — activities push to `ISessionStreamSink` which pushes to the hub which pushes to clients.

### 9.4 WorkflowCatalog repurposed

Keep `WorkflowCatalog.cs` as metadata for the Blazor session-creation dropdown:
```csharp
public record WorkflowCatalogEntry(
    string DisplayName,
    string WorkflowTypeName,        // "SimpleAgentWorkflow"
    string TaskQueue,               // "magicpai-main"
    Type InputType,                 // typeof(SimpleAgentInput) — for form rendering
    string Description,
    bool RequiresAiAssistant,
    string[] SupportedModels);
```

`SessionLaunchPlanner` reads this and builds the right Temporal input.

### 9.5 Studio (Blazor WASM)

**Keep:**
- `Pages/SessionCreatePage.razor` (custom create dialog)
- `Services/SessionApiClient.cs`
- `Services/SessionHubClient.cs`
- `Services/WorkflowInstanceLiveUpdater.cs` — repoint from Elsa Studio events to SignalR hub events
- `Services/BackendUrlResolver.cs`

**Delete (Elsa Studio WASM dependencies):**
- `Program.cs`'s `AddCore/AddShell/AddLoginModule/AddRemoteBackend/AddDashboardModule/AddWorkflowsModule/AddWorkflowsDesigner`
- `ElsaStudioApiKeyHandler`, `MagicPaiFeature`, `MagicPaiMenuProvider`, `MagicPaiMenuGroupProvider`, `MagicPaiWorkflowInstanceObserverFactory`

**Add:**
- A new `Pages/SessionInspectPage.razor` that shows session status + "View execution history in Temporal UI" button (deep-links to `http://localhost:8233/namespaces/magicpai/workflows/{workflowId}/{runId}/history`).
- A lightweight `WorkflowCatalogClient` that fetches `/api/workflows` (new endpoint returning `WorkflowCatalog` entries).

### 9.6 Deep-link scheme to Temporal UI

`MagicPAI.Server/Controllers/WorkflowsController.cs` (new, small):
```csharp
[HttpGet("/api/workflows/{id}/ui-url")]
public IActionResult GetUiUrl(string id)
{
    var ns  = _cfg["Temporal:Namespace"] ?? "magicpai";
    var base = _cfg["Temporal:UiBaseUrl"] ?? "http://localhost:8233";
    return Ok(new { Url = $"{base}/namespaces/{ns}/workflows/{id}" });
}
```

---

## 10. Persistence migration

### 10.1 Elsa EF Core stores → Temporal event store
Elsa wrote workflow definitions + instances + bookmarks to `magicpai` PostgreSQL / SQLite. Temporal wants its **own** database (shared server is fine, separate DB). Running two DBs on one Postgres instance:

- Keep existing `magicpai` DB for **app data only** (sessions table, cost tracking, any user data).
- Create new `temporal` DB (auto-created by Temporal's `auto-setup` image on first run).
- Drop Elsa tables from `magicpai` DB post-migration: `WorkflowDefinitions`, `WorkflowDefinitionPublishers`, `WorkflowInstances`, `Triggers`, `Bookmarks`, `ActivityExecutions`, `WorkflowExecutionLogs`.

### 10.2 Visibility history
Temporal's event history is queryable via `ListWorkflowExecutionsAsync` with visibility filters. For MagicPAI's "past sessions" UI:
- Replace `SessionHistoryReader`'s EF-Core query with a visibility query:
```csharp
await foreach (var w in _client.ListWorkflowsAsync(
    $"WorkflowType='SimpleAgentWorkflow' AND StartTime > '{since:O}'"))
{
    yield return new SessionSummary(w.Id, w.Status, w.StartTime, w.CloseTime);
}
```

### 10.3 Search attributes (optional polish)
Register custom search attributes once:
```bash
temporal operator search-attribute create \
  --name MagicPaiAiAssistant --type Text \
  --name MagicPaiModel --type Text \
  --name MagicPaiSessionKind --type Text
```

Then in workflows:
```csharp
Workflow.UpsertTypedSearchAttributes(
    SearchAttributeUpdate.ValueSet(
        SearchAttributeKey.CreateText("MagicPaiAiAssistant"),
        input.AiAssistant));
```

Enables filtering sessions by model/assistant in our UI list.

---

## 11. Docker-compose — Temporal infrastructure

New file `docker/docker-compose.temporal.yml` (overlay; compose with base):
```yaml
services:
  temporal-db:
    image: postgres:17-alpine
    environment:
      POSTGRES_DB: temporal
      POSTGRES_USER: temporal
      POSTGRES_PASSWORD: temporal
    volumes:
      - temporaldata:/var/lib/postgresql/data
    restart: unless-stopped

  temporal:
    image: temporalio/auto-setup:1.25
    environment:
      - DB=postgres12
      - DB_PORT=5432
      - POSTGRES_USER=temporal
      - POSTGRES_PWD=temporal
      - POSTGRES_SEEDS=temporal-db
      - DYNAMIC_CONFIG_FILE_PATH=/etc/temporal/config/dynamicconfig/development.yaml
      - DEFAULT_NAMESPACE=magicpai
      - DEFAULT_NAMESPACE_RETENTION=72h
    volumes:
      - ./temporal/dynamicconfig:/etc/temporal/config/dynamicconfig
    ports:
      - "7233:7233"     # gRPC
    depends_on:
      - temporal-db
    restart: unless-stopped

  temporal-ui:
    image: temporalio/ui:2.30
    environment:
      - TEMPORAL_ADDRESS=temporal:7233
      - TEMPORAL_CORS_ORIGINS=http://localhost:5000
    ports:
      - "8233:8080"     # Web UI
    depends_on:
      - temporal
    restart: unless-stopped

volumes:
  temporaldata:
```

Modify `docker/docker-compose.yml` `server` service:
```yaml
server:
  # ...existing fields...
  environment:
    - ConnectionStrings__MagicPai=Host=db;Database=magicpai;Username=magicpai;Password=magicpai
    - MagicPAI__UseDocker=true
    - MagicPAI__WorkerImage=magicpai-env:latest
    - Temporal__Host=temporal:7233
    - Temporal__Namespace=magicpai
    - Temporal__UiBaseUrl=http://localhost:8233
  depends_on:
    - db
    - temporal        # NEW
```

Dev workflow:
```bash
# Spin up infra + server
docker compose -f docker/docker-compose.yml -f docker/docker-compose.temporal.yml up -d

# Visit http://localhost:5000 (MagicPAI Studio)
# Visit http://localhost:8233 (Temporal UI)
```

For the inner dev loop (no Docker for the app itself), use the bundled Temporal CLI:
```bash
temporal server start-dev --namespace magicpai --db-filename ./temporal.db
# runs temporal on :7233, UI on :8233
dotnet run --project MagicPAI.Server
```

---

## 12. Phased migration

### Phase 1 — Infrastructure + walking skeleton (2–3 days)
**Goal:** one workflow (`SimpleAgentWorkflow`) runs end-to-end through Temporal on the `temporal` branch, alongside Elsa (Elsa still running).

1. Add `docker/docker-compose.temporal.yml`.
2. Add `Temporalio` + `Temporalio.Extensions.Hosting` packages to `MagicPAI.Server`.
3. In `Program.cs`, add Temporal client + worker alongside the existing Elsa setup.
4. Create `MagicPAI.Activities/Contracts/*.cs` shared input/output records.
5. Port one activity (`DockerActivities.SpawnAsync`, `ExecAsync`, `DestroyAsync`) — Docker group first because every test needs it.
6. Port `AiActivities.RunCliAgentAsync`.
7. Port `SimpleAgentWorkflow`.
8. Add `ISessionStreamSink` + `SignalRSessionStreamSink`.
9. New REST endpoint `POST /api/temporal/sessions` (feature-flagged — coexists with the Elsa version).
10. Run end-to-end through Temporal UI + verify in MagicPAI.Studio.
11. **Checkpoint gate:** a `SimpleAgentWorkflow` session succeeds, streams live output, and appears in both Blazor Studio and Temporal UI.

### Phase 2 — Port everything (5–7 days)
12. Port remaining activities in this order (dependency-driven): Git → Verify → AI (triage/classifier/architect/research/coverage) → Blackboard.
13. Port workflows in order: `VerifyAndRepairWorkflow` → `OrchestrateSimplePathWorkflow` → `OrchestrateComplexPathWorkflow` + `ComplexTaskWorkerWorkflow` → `FullOrchestrateWorkflow` → `DeepResearchOrchestrateWorkflow` → remaining.
14. Rewrite `SessionController` to use `ITemporalClient` for **all** workflows (retire feature flag).
15. Rewrite `SessionHub.ApproveGate` to use Temporal signals.
16. Rewrite `SessionHistoryReader` on top of `ListWorkflowsAsync`.
17. Update Blazor Studio `Program.cs` — remove all Elsa Studio modules, keep custom services + pages.
18. Add `SessionInspectPage.razor` with "View in Temporal UI" deep-link.
19. Port xUnit tests: activities → mock container manager + `WorkflowEnvironment.StartTimeSkippingAsync` for workflow integration tests.
20. **Checkpoint gate:** every named workflow from the old `WorkflowCatalog` runs successfully via the Blazor Studio UI; Temporal UI shows a clean event history for each.

### Phase 3 — Retire Elsa & polish (1–2 days)
21. Remove all Elsa packages and references.
22. Delete `Bridge/ElsaEventBridge`, `Bridge/WorkflowPublisher`, `Bridge/WorkflowProgressTracker`, `Bridge/WorkflowCompletionHandler`, `Providers/MagicPaiActivityDescriptorModifier`, `Workflows/Templates/*.json`, `Workflows/WorkflowBase.cs`, `Workflows/WorkflowBuilderVariableExtensions.cs`, `Workflows/WorkflowInputHelper.cs`.
23. Drop Elsa tables from PostgreSQL/SQLite migration scripts.
24. Update `CLAUDE.md`:
    - Remove "Elsa Activity Rules", "Elsa JSON vs C# Workflow Rules", "Elsa Variable Shadowing Bug" sections
    - Replace with "Temporal Rules" (non-determinism pitfalls, activity timeouts, DI patterns)
    - Update Stack line: `Temporal.io 1.13` instead of `Elsa Workflows 3.6`
    - Update Solution Structure table
25. Update `MAGICPAI_PLAN.md` architecture references.
26. Delete `document_refernce_opensource/elsa-core/`, `document_refernce_opensource/elsa-studio/`. Add `document_refernce_opensource/temporalio-sdk-dotnet/` snapshot.
27. **Final checkpoint:** `dotnet build` succeeds with zero Elsa references. `dotnet test` green. All named workflows verified through MagicPAI.Studio + Temporal UI.

---

## 13. Testing strategy

### 13.1 Unit tests — activities
Mock `IContainerManager`, `ICliAgentFactory`, etc. Use `ActivityExecutionContext` test fixture:
```csharp
using Temporalio.Testing;

[Fact]
public async Task RunCliAgent_ParsesOutput()
{
    var docker = Mock.Of<IContainerManager>(d =>
        d.ExecStreamingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())
         == ToAsyncEnumerable("line1", "line2", "{\"cost\":0.12}"));
    var sut = new AiActivities(factory, docker, sink, auth, creds, NullLogger<AiActivities>.Instance);

    await using var env = ActivityEnvironment.Empty();
    var result = await env.RunAsync(() => sut.RunCliAgentAsync(input));

    Assert.True(result.Success);
    Assert.Equal(0.12m, result.CostUsd);
}
```

### 13.2 Integration tests — workflows
Use `WorkflowEnvironment.StartTimeSkippingAsync()` for fast, real-time-skipping runs:
```csharp
await using var env = await WorkflowEnvironment.StartTimeSkippingAsync();
using var worker = new TemporalWorker(env.Client,
    new TemporalWorkerOptions("test-queue")
        .AddActivity((AiActivities a) => a.RunCliAgentAsync(null!))   // stub the real one
        .AddActivity((DockerActivities a) => a.SpawnAsync(null!))
        .AddWorkflow<SimpleAgentWorkflow>());

await worker.ExecuteAsync(async () =>
{
    var result = await env.Client.ExecuteWorkflowAsync(
        (SimpleAgentWorkflow wf) => wf.RunAsync(new SimpleAgentInput(...)),
        new(id: "wf-1", taskQueue: "test-queue"));
    Assert.True(result.Success);
});
```

### 13.3 Determinism CI gate
Add a `WorkflowReplayer` test that replays checked-in histories against current code. Catches non-determinism introduced by workflow code changes before deploy:
```csharp
var histories = JsonHistory.FromFile("Histories/simple-agent-v1.json");
await new WorkflowReplayer(new()).ReplayWorkflowAsync<SimpleAgentWorkflow>(histories);
```

### 13.4 E2E smoke test
Retain `ContainerLifecycleSmokeTests` + add a full-stack smoke that spins up `temporal server start-dev`, starts the server + worker, hits `POST /api/sessions`, waits for completion, asserts output.

---

## 14. Risks and open questions

| # | Risk | Mitigation |
|---|---|---|
| 1 | **Temporal .NET SDK doesn't officially advertise .NET 10** as of April 2026 (compat via netstandard2.0, but no targeted build) | Dev-test with .NET 10 before Phase 1 starts; fall back to `net9.0` target for the worker-hosting project if any friction (the SDK's Rust-bridge binary is the risk area). |
| 2 | Workflow versioning when we iterate on workflow code while sessions are in flight | Short-lived sessions (most finish in minutes) mean graceful-drain usually suffices. For long-running FullOrchestrate runs, use `Workflow.Patched` per change. Document in CLAUDE.md post-migration. |
| 3 | Losing the visual designer may be resisted by future contributors | Counterargument: designer was causing bugs (variable shadowing, ContainerId propagation); code-first is safer. Document this trade-off in `MAGICPAI_PLAN.md`. |
| 4 | SignalR side-channel and Temporal events can get out of sync (session ends in Temporal but SignalR listeners don't see it) | Subscribe `SessionHub` to Temporal's `GetWorkflowExecutionHistoryAsync` long-poll too; emit "session-closed" once Temporal history shows `WorkflowExecutionCompleted`. |
| 5 | Retry behavior for auth-expired errors is new semantics | Explicitly map `AuthExpiredException` → `ApplicationFailureException(type: "AuthRefreshed", nonRetryable: false)`. Retry policy's default BackoffCoefficient=2 gives time for credential injection. |
| 6 | Temporal UI auth is OIDC-only — no simple API-key auth | For local dev, leave UI open on 8233 (bind to 127.0.0.1 in compose). For prod, configure OIDC via MagicPAI's existing identity provider (if any) or put a reverse proxy in front. |
| 7 | `IterationGateActivity` disappearing means counters live in workflow fields → workflow state grows | Fine — ints are tiny. If any counter grows unbounded, use `Workflow.ContinueAsNewAsync` to reset. |
| 8 | Tests that relied on Elsa's `ActivityExecutionContext` mock | Rewrite against `Temporalio.Testing.ActivityEnvironment`. Straightforward swap. |
| 9 | CLAUDE.md's "E2E Workflow Verification via UI" rule assumed Elsa Studio workflow designer | Update to: verify via MagicPAI.Studio session creation + Temporal UI execution-history inspection. |
| 10 | Large output streaming correctness regressions — if an activity accidentally returns stdout in its output record | Enforce via code review + lint rule (activity output records must be small records with no `string` fields > 10 KB); CI sanity-check activity return payload sizes. |

---

## 15. Success criteria

Migration is "done" when **all** of these are true:

- [ ] `dotnet build MagicPAI.sln` — zero `Elsa.*` references, zero warnings.
- [ ] `dotnet test` — all tests green.
- [ ] `docker compose up` brings up: MagicPAI server, Postgres (magicpai DB), Temporal server, Temporal UI. No Elsa processes running.
- [ ] From the browser at `http://localhost:5000`, user can:
  - [ ] Create a session for every workflow in the catalog (`SimpleAgent`, `FullOrchestrate`, `OrchestrateComplexPath`, `OrchestrateSimplePath`, `PromptEnhancer`, `ContextGatherer`, `PromptGrounding`, `PostExecutionPipeline`, `ResearchPipeline`, `StandardOrchestrate`, `ClawEvalAgent`, `WebsiteAuditCore`, `WebsiteAuditLoop`, `VerifyAndRepair`, `DeepResearchOrchestrate`).
  - [ ] Live-stream CLI output via SignalR.
  - [ ] Cancel a running session.
  - [ ] Approve a gate (human-in-the-loop workflow that uses signals).
  - [ ] Click "View in Temporal UI" → land on the correct execution in Temporal Web.
- [ ] Every AI/CLI activity provably runs inside a Docker container (spawn/exec/destroy trio).
- [ ] Temporal UI shows clean event history for every completed run (no non-determinism warnings).
- [ ] `CLAUDE.md`, `MAGICPAI_PLAN.md` updated.
- [ ] `document_refernce_opensource/` updated with Temporal SDK snapshot; Elsa snapshot removed.

---

## 16. Appendices

### Appendix A — Full NuGet diff

`MagicPAI.Core.csproj` — **no change.**

`MagicPAI.Activities.csproj`:
```diff
- <PackageReference Include="Elsa.Workflows" Version="3.6.0" />
- <PackageReference Include="Elsa.Workflows.Core" Version="3.6.0" />
+ <PackageReference Include="Temporalio" Version="1.13.0" />
```

`MagicPAI.Workflows.csproj`:
```diff
- <PackageReference Include="Elsa.Workflows" Version="3.6.0" />
- <PackageReference Include="Elsa.Workflows.Core" Version="3.6.0" />
- <PackageReference Include="Elsa.Workflows.Management" Version="3.6.0" />
+ <PackageReference Include="Temporalio" Version="1.13.0" />
```

`MagicPAI.Server.csproj`:
```diff
- <PackageReference Include="Elsa" Version="3.6.0" />
- <PackageReference Include="Elsa.Workflows.Api" Version="3.6.0" />
- <PackageReference Include="Elsa.Workflows.Management" Version="3.6.0" />
- <PackageReference Include="Elsa.Workflows.Runtime" Version="3.6.0" />
- <PackageReference Include="Elsa.EntityFrameworkCore" Version="3.6.0" />
- <PackageReference Include="Elsa.EntityFrameworkCore.PostgreSql" Version="3.6.0" />
- <PackageReference Include="Elsa.EntityFrameworkCore.Sqlite" Version="3.6.0" />
- <PackageReference Include="Elsa.Http" Version="3.6.0" />
- <PackageReference Include="Elsa.Scheduling" Version="3.6.0" />
- <PackageReference Include="Elsa.JavaScript" Version="3.6.0" />
- <PackageReference Include="Elsa.Identity" Version="3.6.0" />
+ <PackageReference Include="Temporalio" Version="1.13.0" />
+ <PackageReference Include="Temporalio.Extensions.Hosting" Version="1.13.0" />
+ <PackageReference Include="Temporalio.Extensions.OpenTelemetry" Version="1.13.0" />
```

`MagicPAI.Studio.csproj`:
```diff
- <PackageReference Include="Elsa.Studio.Core.BlazorWasm" Version="3.6.0" />
- <PackageReference Include="Elsa.Studio.Shell" Version="3.6.0" />
- <PackageReference Include="Elsa.Studio.Login.BlazorWasm" Version="3.6.0" />
- <PackageReference Include="Elsa.Studio.Dashboard" Version="3.6.0" />
- <PackageReference Include="Elsa.Studio.Workflows" Version="3.6.0" />
- <PackageReference Include="Elsa.Studio.Workflows.Designer" Version="3.6.0" />
- <PackageReference Include="Elsa.Api.Client" Version="3.6.0" />
(no Temporal packages needed — Blazor WASM talks to the server over REST/SignalR only)
```

### Appendix B — File delete list (end of Phase 3)

```
MagicPAI.Activities/AI/RunCliAgentActivity.cs          → replaced by AiActivities.cs
MagicPAI.Activities/AI/AiAssistantActivity.cs          → deleted (alias)
MagicPAI.Activities/AI/TriageActivity.cs               → replaced
MagicPAI.Activities/AI/ClassifierActivity.cs           → replaced
MagicPAI.Activities/AI/ModelRouterActivity.cs          → replaced
MagicPAI.Activities/AI/PromptEnhancementActivity.cs    → replaced
MagicPAI.Activities/AI/ArchitectActivity.cs            → replaced
MagicPAI.Activities/AI/ResearchPromptActivity.cs       → replaced
MagicPAI.Activities/AI/WebsiteTaskClassifierActivity.cs → replaced
MagicPAI.Activities/AI/RequirementsCoverageActivity.cs → replaced
MagicPAI.Activities/Docker/*.cs                        → replaced by DockerActivities.cs
MagicPAI.Activities/Git/*.cs                           → replaced by GitActivities.cs
MagicPAI.Activities/Verification/*.cs                  → replaced by VerifyActivities.cs
MagicPAI.Activities/ControlFlow/IterationGateActivity.cs → deleted (inline in workflows)
MagicPAI.Activities/Infrastructure/HumanApprovalActivity.cs → deleted (use signals)
MagicPAI.Activities/Infrastructure/UpdateCostActivity.cs  → deleted (inline)
MagicPAI.Activities/Infrastructure/EmitOutputChunkActivity.cs → deleted (use ISessionStreamSink)
MagicPAI.Activities/Infrastructure/ClaimFileActivity.cs   → replaced by BlackboardActivities.cs

MagicPAI.Server/Workflows/Templates/*.json             → all 23 files deleted
MagicPAI.Server/Workflows/WorkflowBase.cs              → deleted
MagicPAI.Server/Workflows/WorkflowBuilderVariableExtensions.cs → deleted
MagicPAI.Server/Workflows/WorkflowInputHelper.cs       → deleted
MagicPAI.Server/Workflows/TestSetPromptWorkflow.cs     → deleted
MagicPAI.Server/Workflows/TestClassifierWorkflow.cs    → deleted
MagicPAI.Server/Workflows/TestWebsiteClassifierWorkflow.cs → deleted
MagicPAI.Server/Workflows/TestPromptEnhancementWorkflow.cs → deleted
MagicPAI.Server/Workflows/TestFullFlowWorkflow.cs      → deleted
MagicPAI.Server/Workflows/IsComplexAppWorkflow.cs      → deleted (merged into orchestrators)
MagicPAI.Server/Workflows/IsWebsiteProjectWorkflow.cs  → deleted
MagicPAI.Server/Workflows/LoopVerifierWorkflow.cs      → deleted (inlined loop)
(all other workflows: rewritten in place, same filename)

MagicPAI.Server/Bridge/ElsaEventBridge.cs              → deleted
MagicPAI.Server/Bridge/WorkflowPublisher.cs            → deleted
MagicPAI.Server/Bridge/WorkflowCompletionHandler.cs    → deleted
MagicPAI.Server/Bridge/WorkflowProgressTracker.cs      → deleted
MagicPAI.Server/Providers/MagicPaiActivityDescriptorModifier.cs → deleted

MagicPAI.Studio/Services/MagicPaiFeature.cs            → deleted
MagicPAI.Studio/Services/MagicPaiMenuProvider.cs       → deleted
MagicPAI.Studio/Services/MagicPaiMenuGroupProvider.cs  → deleted
MagicPAI.Studio/Services/MagicPaiWorkflowInstanceObserverFactory.cs → deleted
MagicPAI.Studio/Services/ElsaStudioApiKeyHandler.cs    → deleted
MagicPAI.Studio/Services/DummyAuthHandler.cs           → keep or delete per actual usage

document_refernce_opensource/elsa-core/                → deleted
document_refernce_opensource/elsa-studio/              → deleted
document_refernce_opensource/docs/                     → audit, keep only generic workflow-theory docs
```

### Appendix C — First commit after this plan lands

```
temporal: add migration plan (Phase 0)

No code changes. Full plan lives in TEMPORAL_MIGRATION_PLAN.md:
- Concept mapping (Elsa → Temporal)
- 32 activities → 19 [Activity] methods + 5 inlines
- 24 workflows → 15 [Workflow] classes + 9 deletions
- Docker-always invariant preserved
- Studio: keep custom Blazor, drop Elsa Studio, deep-link to Temporal UI
- docker-compose overlay for temporal + temporal-ui
- Phased rollout (Phase 1 walking skeleton → Phase 2 full port → Phase 3 retire Elsa)
```
