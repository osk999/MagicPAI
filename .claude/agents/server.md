---
name: server
description: Build MagicPAI.Server and MagicPAI.Workflows
isolation: worktree
---

You are building **MagicPAI.Server** (ASP.NET Core host) and **MagicPAI.Workflows** (built-in workflow templates).

## Your Scope (ONLY touch these files)
- `MagicPAI.Server/**`
- `MagicPAI.Workflows/**`

## Prerequisites
Wait until `MagicPAI.Core` and `MagicPAI.Activities` are built.

## What to Build

Read `MAGICPAI_PLAN.md` for detailed specifications.

### Step 1: MagicPAI.Server/Program.cs

See MAGICPAI_PLAN.md Section 8.2. Configure:
- MagicPAI services (SharedBlackboard, IContainerManager, ICliAgentFactory, VerificationPipeline, MagicPaiConfig)
- SignalR
- Elsa (WorkflowManagement with SQLite, WorkflowRuntime, Identity, WorkflowsApi, Http)
- Register all activities from MagicPAI.Activities assembly: `elsa.AddActivitiesFrom<RunCliAgentActivity>()`
- Register built-in workflows: `elsa.AddWorkflow<FullOrchestrateWorkflow>()`
- Blazor WASM middleware: `app.UseBlazorFrameworkFiles()`, `app.UseStaticFiles()`, `app.MapFallbackToFile("index.html")`
- SignalR hub: `app.MapHub<SessionHub>("/hub")`
- Elsa middleware: `app.UseWorkflowsApi()`, `app.UseWorkflows()`
- DB migration: `await app.Services.MigrateElsaDatabaseAsync()`

### Step 2: MagicPAI.Server/appsettings.json

See MAGICPAI_PLAN.md Section 19.2. Include ConnectionStrings, MagicPAI config, Elsa Identity.

### Step 3: MagicPAI.Server/Hubs/SessionHub.cs

Simplified SignalR hub (~200 lines). Methods:
- `CreateSession(prompt, workspacePath, agent, model)` — dispatches Elsa workflow, returns instance ID
- `StopSession(sessionId)` — cancels workflow instance
- `Approve(sessionId, decision)` — resumes bookmark
- `GetSessionOutput(sessionId)` — returns buffered output

Use `IWorkflowDispatcher` to dispatch workflows and `IBookmarkResumer` to resume bookmarks.

### Step 4: MagicPAI.Server/Bridge/ElsaEventBridge.cs

See MAGICPAI_PLAN.md Phase 4. Implements `INotificationHandler<ActivityExecutionLogUpdated>`.
Listens for execution log entries with event names: OutputChunk, VerificationUpdate, TaskDispatched, TaskCompleted.
Forwards to SignalR hub via `IHubContext<SessionHub>`.

### Step 5: MagicPAI.Server/Bridge/WorkflowProgressTracker.cs

Tracks workflow execution progress. Implements `INotificationHandler<WorkflowExecuted>`.
Sends `workflowProgress` and `sessionStateChanged` events to SignalR.

### Step 6: MagicPAI.Server/Controllers/SessionController.cs

REST API. See MAGICPAI_PLAN.md Section 17.2.
- POST `/api/sessions` — create session (dispatch workflow)
- GET `/api/sessions` — list sessions
- GET `/api/sessions/{id}` — get state
- DELETE `/api/sessions/{id}` — stop session
- POST `/api/sessions/{id}/approve` — resume from bookmark

### Step 7: MagicPAI.Workflows/FullOrchestrateWorkflow.cs

See MAGICPAI_PLAN.md Section 12.2. Elsa WorkflowBase with Flowchart root.
Flow: SpawnContainer → Triage → (Simple: RunCliAgent | Complex: Architect → ForEach parallel tasks → VerifyRepair) → DestroyContainer

### Step 8: MagicPAI.Workflows/SimpleAgentWorkflow.cs

Simple flow: SpawnContainer → RunCliAgent → RunVerification → DestroyContainer

### Step 9: MagicPAI.Workflows/VerifyAndRepairWorkflow.cs

Reusable sub-workflow. See MAGICPAI_PLAN.md Section 12.3.
RunVerification → While(!passed && attempt < max) { Repair → RunCliAgent → RunVerification }

## Rules
- Run `dotnet build` after each step
- SessionHub should be ~200 lines max — keep it simple
- Use Elsa's native IWorkflowDispatcher, never execute workflows manually
