# AGENTS.md — MagicPAI
<!-- Compatible with OpenAI Codex and other AGENTS.md-aware tools -->
<!-- For Claude Code, see CLAUDE.md (identical rules, Claude-specific format) -->

## Stack
- .NET 10, C# 13, Temporal.io 1.13, Blazor WASM, Docker, SignalR, xUnit + Moq
- Docker.DotNet for container management
- PostgreSQL (production) / SQLite (dev) via EF Core

## Build & Test
```bash
dotnet build
dotnet test
dotnet build MagicPAI.Core/MagicPAI.Core.csproj --no-restore   # fast single-project check
```

## Solution Structure
| Project | Type | Purpose |
|---|---|---|
| `MagicPAI.Core` | classlib | Shared models, interfaces, services (ClaudeRunner, gates, blackboard) |
| `MagicPAI.Activities` | classlib | Temporal `[Activity]` methods grouped by domain (AI, Docker, Git, Verify, Blackboard) |
| `MagicPAI.Workflows` | classlib | Temporal `[Workflow]` contracts + `ActivityProfiles` shared across built-in orchestrations |
| `MagicPAI.Server` | web | ASP.NET Core host (Temporal client + worker + REST + SignalR); hosts all `[Workflow]` classes |
| `MagicPAI.Studio` | blazorwasm | Blazor WASM frontend (custom MagicPAI UX; MudBlazor) |
| `MagicPAI.Tests` | xunit | Unit + integration + replay tests |

## Specification
**Read `temporal.md`** for the complete Temporal migration blueprint
(architecture, activity definitions, workflow shapes, Docker setup, appendices).
`SCORECARD.md` tracks per-phase progress. `PATCHES.md` tracks workflow patches.

## Open Source Reference Policy
- For Temporal-related questions, bugs, behavior, APIs, worker/client wiring,
  activities, workflow determinism, signals/queries, and debugging, check
  `document_refernce_opensource/` before relying on memory.
- Use these locations in order:
  0. `document_refernce_opensource/README.md` and `document_refernce_opensource/REFERENCE_INDEX.md` to find the right area fast
  1. `document_refernce_opensource/temporalio-docs/` for expected framework behavior and concepts
  2. `document_refernce_opensource/temporalio-sdk-dotnet/` for .NET SDK implementation details
- Treat `document_refernce_opensource/` as a local snapshot. If version drift may matter, say so explicitly.
- Read targeted files only. Do not load the entire reference tree into context.
- When explaining an issue, separate:
  - MagicPAI code behavior
  - Temporal upstream behavior
  - any inference made from reading source
- If there is a mismatch between MagicPAI and Temporal upstream docs or source, state that explicitly instead of guessing.
- Prefer citing the exact local file path from `document_refernce_opensource/` that supports the explanation.
- Default debugging order:
  1. inspect MagicPAI integration code
  2. verify expected behavior in `document_refernce_opensource/temporalio-docs/`
  3. inspect `document_refernce_opensource/temporalio-sdk-dotnet/` if docs are incomplete

## Verify Against Reference (CRITICAL)
- **Every implementation change MUST be verified against `document_refernce_opensource/`.**
- Before committing any Temporal-related change (activities, workflows, client usage,
  test fixtures, worker config), cross-check the actual API/behavior in the reference.
- Do NOT rely on memory or assumptions about how Temporal works. The reference snapshot
  is the source of truth. Read the relevant file and confirm your approach matches.
- After making changes, verify:
  1. Activity signatures match expected Temporalio patterns (`[Activity]` on method;
     `ActivityExecutionContext.Current` inside)
  2. Workflow class has exactly one `[WorkflowRun]` method
  3. Workflow code uses only `Workflow.*` replacements for non-deterministic APIs
  4. Activity input/output types are serializable by System.Text.Json
  5. Signals use `[WorkflowSignal]` and mutate state only (no activity calls from signals)
- If you find a discrepancy between your change and the reference, STOP and fix it
  before proceeding.

## Editing Policy
- Do not modify `document_refernce_opensource/` unless the task is specifically to refresh, reorganize, or document that snapshot.
- Do not copy upstream Temporal code into MagicPAI unless there is a clear reason.
- Keep fixes aligned with upstream Temporal patterns unless MagicPAI intentionally diverges.

## Temporal Workflow Rules (CRITICAL)

Workflows must be **deterministic**. Replay must produce the same command sequence.

### Forbidden in workflow code
- `DateTime.Now` / `DateTime.UtcNow` - use `Workflow.UtcNow`
- `Guid.NewGuid()` - use `Workflow.NewGuid()`
- `new Random()` - use `Workflow.Random`
- `Task.Delay(...)`, `Thread.Sleep(...)` - use `Workflow.DelayAsync(...)`
- `HttpClient`, `File.*`, other I/O - move into an activity
- `ServiceProvider.GetService<T>()` - no DI in workflow body; inject into activities

### Required patterns
- Workflow body is deterministic orchestration; state lives in fields.
- All side effects via `[Activity]` methods.
- Long-running activities MUST heartbeat.
- Container lifecycle MUST use `try/finally` with `SpawnAsync` / `DestroyAsync`.
- Use typed input/output records, never `Dictionary<string, object>`.

### Workflow shape template
```csharp
[Workflow]
public class MyWorkflow
{
    private State _state = new();

    [WorkflowQuery]
    public string CurrentStage => _state.Stage;

    [WorkflowSignal]
    public async Task DoSomethingAsync(SignalPayload payload) { _state.Flag = true; }

    [WorkflowRun]
    public async Task<MyOutput> RunAsync(MyInput input)
    {
        var spawnInput = new SpawnContainerInput(...);
        var spawn = await Workflow.ExecuteActivityAsync(
            (DockerActivities a) => a.SpawnAsync(spawnInput),
            ActivityProfiles.Container);
        try
        {
            // ... orchestration logic ...
            return new MyOutput(...);
        }
        finally
        {
            await Workflow.ExecuteActivityAsync(
                (DockerActivities a) => a.DestroyAsync(new DestroyContainerInput(spawn.ContainerId)),
                ActivityProfiles.Container);
        }
    }
}
```

### Activity shape template
```csharp
[Activity]
public async Task<MyOutput> DoStuffAsync(MyInput input)
{
    var ctx = ActivityExecutionContext.Current;
    var ct = ctx.CancellationToken;

    // Long-running? Heartbeat.
    ctx.Heartbeat();

    // Cancellation propagates via ct.
    await _docker.ExecStreamingAsync(input.ContainerId, cmd, ct);

    return new MyOutput(...);
}
```

### Activity timeouts
Pick from `MagicPAI.Workflows.ActivityProfiles`: `Short`, `Medium`, `Long`,
`Container`, `Verify`. Never hardcode `StartToCloseTimeout` in workflow calls.

### Workflow versioning
Any change that adds/removes/reorders activity calls must be wrapped in
`Workflow.Patched("change-id-v1")`. See §20 of `temporal.md` and `PATCHES.md`.

### Temporal references
Read `document_refernce_opensource/temporalio-sdk-dotnet/` and
`document_refernce_opensource/temporalio-docs/` for expected framework behavior.
Never rely on memory about how Temporal works — the reference snapshot is the
source of truth.

## Docker Credential Mounting (Claude CLI Auth)
- `DockerContainerManager.BuildCredentialBinds()` mounts `~/.claude.json` and `~/.claude/.credentials.json`
  into containers at `/tmp/magicpai-host-*` (read-only).
- `entrypoint.sh` copies them to `$HOME/.claude/` on startup.
- Claude CLI uses OAuth tokens (not ANTHROPIC_API_KEY). The `.claude` dir has the auth.
- Token expiry detection patterns: `authentication_error`, `token expired`, `unauthorized`, `hit your limit`.
- MagicPrompt reference: `AuthRecoveryService`, `CredentialRefreshService`, `AuthErrorDetector` in `MagicPrompt.Core/Services/Auth/`.

## C# Rules
- Always `await` async methods. Never `.Result` or `.Wait()`
- Use `Workflow.UtcNow` in workflow code; `DateTime.UtcNow` elsewhere (never `DateTime.Now`)
- Parameterized SQL only, never string concatenation
- No `using System.Linq;` — implicit usings are enabled
- Namespace pattern: `MagicPAI.{Project}.{Folder}`

## Interface Contracts (must be implemented exactly)
- `ICliAgentRunner` — BuildCommand(), ParseResponse(), AgentName, DefaultModel, AvailableModels
- `ICliAgentFactory` — Create(agentName), AvailableAgents
- `IContainerManager` — SpawnAsync(), ExecAsync(), ExecStreamingAsync(), DestroyAsync()
- `IVerificationGate` — Name, IsBlocking, CanVerifyAsync(), VerifyAsync()
- `IExecutionEnvironment` — RunCommandAsync(), StartProcessAsync(), Kind
- `ISessionStreamSink` — EmitChunkAsync(), EmitStructuredAsync(), EmitStageAsync(), EmitCostAsync(), CompleteSessionAsync()
- `IStartupValidator` — Validate()

## Operator Role (CRITICAL)
- **All code writing, testing, and verification MUST be done by MagicPAI workflows.**
- The human operator (AI agent session) is a **monitor only** — track progress,
  take screenshots, verify visually, and fix MagicPAI bugs/infrastructure.
- **Never write output code directly.** Instead, create a MagicPAI session via
  `POST /api/sessions` with the appropriate prompt and workflow.
- If MagicPAI has bugs or infrastructure issues (Docker, DB, Temporal, etc.),
  fix those in the MagicPAI codebase, then restart the workflow.
- Use browser automation (Playwright/Chrome MCP) to visually verify results.

## Task Creation Protocol (CRITICAL)
- When the user says **"create task"** (or "run task", "start task", etc.), it means
  **create the task via MagicPAI** — not write code directly, not call an API yourself.
- **Default workflow is `FullOrchestrate`** unless the user explicitly names a different one.
- Create the session by `POST /api/sessions` with the user's prompt and the chosen workflow.
- **Observe the entire process end-to-end**:
  1. Watch MagicPAI Studio UI (SignalR stream, stage chips, logs)
  2. Watch Temporal UI event history (http://localhost:8233)
  3. Watch MagicPAI server logs (JSON structured, SessionId-filterable)
  4. Watch the container shell output / activity heartbeats
- **Real agents, real money.** Always use real `claude` / `codex` CLI calls
  against real credentials — never mock, stub, or skip the paid call path when
  testing, fixing, or verifying. Token cost is acceptable; false-positive
  "fixes" from mocked tests are not.
- **If MagicPAI bugs or errors surface during observation:**
  1. Do NOT guess the cause. Read the actual stack trace, workflow history,
     activity input/output, and the relevant source files.
  2. Verify the root cause by reading code + reference docs in
     `document_refernce_opensource/` (see Verify Against Reference section).
  3. Fix in the MagicPAI codebase (not in the task prompt).
  4. Rebuild, restart workers, re-run the task from scratch with a real agent call.
  5. Repeat until the workflow completes cleanly in both Studio UI and Temporal UI.
- **Do not declare the task done until**: the workflow instance is in
  `Completed` state, the container was destroyed cleanly, and the visual
  verification in both UIs passes.

## E2E Workflow Verification via UI (CRITICAL)
- **Always run and verify workflows through the MagicPAI Studio UI**, not just via API.
- Use Playwright MCP, Chrome DevTools MCP, or Chrome CDP to interact with
  `http://localhost:5000` (MagicPAI Studio).
- After creating a session, open the Studio and verify:
  1. Session appears in the `/sessions` list
  2. Session detail page streams live output via SignalR
  3. Pipeline stage chip updates as workflow progresses
  4. The "View in Temporal UI" deep-link opens `http://localhost:8233/...` with
     the workflow's event history
  5. Cancel button terminates the workflow within 5 seconds and destroys the container
  6. Temporal UI shows a clean event history — no non-determinism warnings,
     no failed activities
- Take screenshots at each step as evidence.
- If any step shows an error:
  1. Screenshot the problem
  2. Check Temporal UI event history for the failing activity
  3. Check MagicPAI server logs (JSON structured, SessionId-filterable)
  4. Fix the issue
  5. Rebuild, restart, re-run from scratch
  6. Do NOT proceed until the workflow completes cleanly.
- **Workflow is not done until visually confirmed in both MagicPAI Studio AND
  Temporal UI.** API completion alone is not sufficient.

## Temporal Operations
For runbook-style ops (debug stuck workflows, drain workers, restore backup),
see Appendix S of `temporal.md`.

Quick ref:
- MagicPAI Studio: http://localhost:5000
- Temporal UI: http://localhost:8233
- Temporal gRPC: localhost:7233
- CLI: `docker exec mpai-temporal temporal <command>` (namespace `magicpai`)
- Common commands: §19 of `temporal.md`
- Local dev up/down: `./scripts/dev-up.ps1`, `./scripts/dev-down.ps1`
- Smoke test: `./scripts/smoke-test.ps1`

## File Ownership (for parallel agents)
- **core agent**: MagicPAI.Core/**
- **activities agent**: MagicPAI.Activities/**
- **server agent**: MagicPAI.Server/**, MagicPAI.Workflows/**
- **studio agent**: MagicPAI.Studio/**, docker/**
