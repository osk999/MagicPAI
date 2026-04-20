---
name: server
description: Build MagicPAI.Server (ASP.NET host + Temporal client/worker + REST + SignalR) and MagicPAI.Workflows (contracts + ActivityProfiles)
isolation: worktree
---

You are working on **MagicPAI.Server** (ASP.NET Core host + Temporal client + worker + REST API + SignalR hub) and **MagicPAI.Workflows** (workflow contracts + `ActivityProfiles`).

## Your scope (ONLY touch these files)
- `MagicPAI.Server/**`
- `MagicPAI.Workflows/**`
- `MagicPAI.Tests/Workflows/**` (integration + replay tests for workflows)
- `MagicPAI.Tests/Server/**` (controller / bridge / service tests)

## Prerequisites
- `MagicPAI.Core` and `MagicPAI.Activities` must be compiling.
- Read `CLAUDE.md` top to bottom — it's the current source of truth for Temporal rules.

## What lives here

### MagicPAI.Server/
- `Program.cs` — registers Temporal client + worker, activities (`AddScopedActivities<T>()`), workflows (`AddWorkflow<T>()`), hosted services (`SearchAttributesInitializer`, `WorkflowCompletionMonitor`, `WorkerPodGarbageCollector`), and core services. No Elsa anywhere.
- `Workflows/*.cs` — 16 `[Workflow]` classes in `MagicPAI.Server.Workflows` namespace.
- `Controllers/SessionController.cs` — unified `/api/sessions` dispatching all workflow types via `ITemporalClient.StartWorkflowAsync`.
- `Controllers/BrowseController.cs` — file-browse endpoint (unchanged from pre-migration).
- `Hubs/SessionHub.cs` — SignalR hub; `ApproveGate`/`RejectGate`/`InjectPrompt`/`CancelSession` all dispatch Temporal signals.
- `Bridge/WorkflowCatalog.cs` — list of 16 workflow catalog entries (DisplayName, WorkflowTypeName, TaskQueue, InputType, Category, etc.).
- `Services/SessionLaunchPlanner.cs` — translates `CreateSessionRequest` to the correct typed input record per workflow.
- `Bridge/SessionHistoryReader.cs` — queries Temporal's visibility store via `ITemporalClient.ListWorkflowsAsync`.
- `Services/DockerEnforcementValidator.cs` — startup validator rejecting non-Docker backends.
- `Services/SearchAttributesInitializer.cs` — idempotent search-attribute registration on worker startup.
- `Services/WorkflowCompletionMonitor.cs` — hosted service polling for terminal state → emits SignalR `SessionCompleted`.
- `Services/SignalRSessionStreamSink.cs` — implements `ISessionStreamSink` for activity-side CLI streaming.
- `Services/MagicPaiMetrics.cs` — OpenTelemetry counters/histograms.

### MagicPAI.Workflows/
- `Contracts/*.cs` — one file per workflow, each containing input/output records.
- `Contracts/Common.cs` — shared records (`ModelSpec`, `SessionContext`, `VerifyGateSpec`, `VerifyResult`, `CostEntry`).
- `ActivityProfiles.cs` — `Short` / `Medium` / `Long` / `Container` / `Verify` `ActivityOptions` presets.

## Specifications

- **Plan:** `temporal.md` (master doc; §9 server layer, §M.1 consolidated Program.cs, §M.2 WorkflowCatalog, §M.3 SessionLaunchPlanner, §M.4 SessionController, §H + §8 workflow templates).
- **Day-by-day guides:** `docs/phase-guides/Phase1-Day3.md` (first workflow E2E), `Phase2-Day6.md` through `Phase2-Day10.md` (workflow port + server unification).

## Key Temporal patterns (do not violate)

- Workflows in `MagicPAI.Server/Workflows/` are **pure deterministic orchestrations**. Never use `DateTime.UtcNow`, `Guid.NewGuid`, `Random`, `Task.Delay`, `Thread.Sleep`, I/O, or DI resolution inside workflow bodies. Always use `Workflow.UtcNow` / `Workflow.NewGuid()` / `Workflow.Random` / `Workflow.DelayAsync` / `Workflow.WhenAllAsync` / `Workflow.WhenAnyAsync`.
- All workflows have: `[Workflow]` on the class, exactly one `[WorkflowRun]` method. Signals via `[WorkflowSignal]`. Queries via `[WorkflowQuery]`.
- Use `ActivityProfiles.*` for timeouts — never hardcode `StartToCloseTimeout`.
- **Expression-tree constraint:** build activity/child-workflow input records in local variables BEFORE the `Workflow.ExecuteActivityAsync` / `Workflow.ExecuteChildWorkflowAsync` lambda (CS9307 if using named args inside the lambda).
- Child workflow cancellation: use `CancellationTokenSource` + `ChildWorkflowOptions.CancellationToken` pattern (see `OrchestrateComplexPathWorkflow.cs` for reference).
- Every workflow MUST have at least one integration test (`WorkflowEnvironment.StartTimeSkippingAsync()` + stubs) and one replay test (`WorkflowReplayer` against a captured history in `MagicPAI.Tests/Workflows/Histories/<kebab>/`).

## Server wiring patterns

- Register workflows with `.AddWorkflow<MagicPAI.Server.Workflows.XxxWorkflow>()`.
- Register activity classes with `.AddScopedActivities<T>()` once per class (methods auto-picked-up).
- All workflows share one task queue: `magicpai-main`.
- Search attributes registered in `Program.cs` via `SearchAttributesInitializer` (hosted service).
- `DockerEnforcementValidator.Validate()` runs immediately after `builder.Build()` — throws if `ExecutionBackend != "docker"`.

## Don't do

- Don't re-introduce Elsa. The migration is complete; Elsa infrastructure is gone.
- Don't put I/O or DI resolution inside a `[Workflow]` method body.
- Don't route large CLI stdout through activity return values — use `ISessionStreamSink`.
- Don't suppress `Workflow.Patched`-style versioning if you're changing in-flight workflow shape.
- Don't modify `MagicPAI.Core` — it's frozen.

## After changes

1. `dotnet build MagicPAI.Server/MagicPAI.Server.csproj` — 0 errors.
2. `dotnet test MagicPAI.Tests --filter "Category=Integration|Category=Replay"` — green.
3. Optionally start server (`dotnet run --project MagicPAI.Server`) and hit `POST /api/sessions` for end-to-end verification.
4. Update `SCORECARD.md`.
5. If you changed an in-flight workflow's command sequence, document the patch in `PATCHES.md` (even though migration is done, this log continues forever).
