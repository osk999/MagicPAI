---
name: activities
description: Build MagicPAI.Activities — Temporal [Activity] methods grouped by domain (AI, Docker, Git, Verify, Blackboard)
isolation: worktree
---

You are working on **MagicPAI.Activities** — Temporal activity classes.

## Your scope (ONLY touch these files)
- `MagicPAI.Activities/**`
- `MagicPAI.Tests/Activities/**` (when adding/updating tests)

## Prerequisites
- `MagicPAI.Core` interfaces and models must exist (they already do; do not modify Core).
- Read `MagicPAI.Core/Services/` to understand the real `IContainerManager` / `ICliAgentFactory` / `SharedBlackboard` / `VerificationPipeline` signatures before writing activity bodies.

## What to build

All activities are plain methods on one of these classes — never one-class-per-activity:

| Class | Methods | Path |
|---|---|---|
| `AiActivities` | `RunCliAgentAsync`, `TriageAsync`, `ClassifyAsync`, `RouteModelAsync`, `EnhancePromptAsync`, `ArchitectAsync`, `ResearchPromptAsync`, `ClassifyWebsiteTaskAsync`, `GradeCoverageAsync` | `MagicPAI.Activities/AI/AiActivities.cs` |
| `DockerActivities` | `SpawnAsync`, `ExecAsync`, `StreamAsync`, `DestroyAsync` | `MagicPAI.Activities/Docker/DockerActivities.cs` |
| `GitActivities` | `CreateWorktreeAsync`, `MergeWorktreeAsync`, `CleanupWorktreeAsync` | `MagicPAI.Activities/Git/GitActivities.cs` |
| `VerifyActivities` | `RunGatesAsync`, `GenerateRepairPromptAsync` | `MagicPAI.Activities/Verification/VerifyActivities.cs` |
| `BlackboardActivities` | `ClaimFileAsync`, `ReleaseFileAsync` | `MagicPAI.Activities/Infrastructure/BlackboardActivities.cs` |

Input/output records live in `MagicPAI.Activities/Contracts/*.cs` (one file per class group).

## Specifications (authoritative)

- **Detailed code templates:** `temporal.md` §I.1-I.4 + §7.2-7.6 (contracts).
- **Activity shape template:** `temporal.md` Appendix R (inside CLAUDE.md section).
- **Day-by-day work plan:** `docs/phase-guides/Phase2-Day4.md` (AI part 1), `Phase2-Day5.md` (remaining).

## Key Temporal patterns (do not violate)

1. Method attribute: `[Temporalio.Activities.Activity]` (fully-qualified to avoid Elsa-era collision if any remains).
2. Method signature: `public async Task<TOut> NameAsync(TIn input)`. Use typed record input/output — never `Dictionary<string, object>`.
3. Inside activity body:
   - Get context via `ActivityExecutionContext.Current`.
   - Get cancellation via `ctx.CancellationToken`.
   - Long-running? Call `ctx.Heartbeat(offset)` every ~20 stream lines or ~30s.
   - For retries that need resume state, use `ctx.Info.HeartbeatDetails`.
4. DI via constructor — never `context.GetRequiredService<T>()` (that's the Elsa pattern).
5. Errors:
   - Transient → throw normally; Temporal retries.
   - Non-retryable → `throw new ApplicationFailureException(message, type: "ConfigError", nonRetryable: true)`.
   - Cancellation → let `OperationCanceledException` propagate (clean up containers first in catch).
6. Never output raw CLI stdout in the return value. Emit to `ISessionStreamSink` side-channel for SignalR streaming; return only small summary fields.

## Docker invariant (never violate)

Per `temporal.md` §11: every AI/CLI activity MUST execute inside a Docker container. `DockerActivities.SpawnAsync` is the only way to get a container ID; every downstream activity takes `ContainerId` as required input.

## Testing

Every new activity method needs at least one unit test in `MagicPAI.Tests/Activities/` using `Temporalio.Testing.ActivityEnvironment` + Moq-ed `IContainerManager` / `ICliAgentFactory`. Tag `[Trait("Category","Unit")]`.

See `temporal.md` §15.3 (unit test template) and `docs/phase-guides/Phase2-Day4.md` Step 4 for examples.

## Registration (coordinate with server agent)

The server agent registers each class via `.AddScopedActivities<TClass>()` in `Program.cs`. New methods on an already-registered class are picked up automatically. New classes need a new `.AddScopedActivities<T>()` call — ping the server agent.

## Don't do

- Don't use Elsa base class `Activity` or Elsa `Input<T>` / `Output<T>` — those are gone.
- Don't use `[FlowNode("Done","Failed")]` — Temporal uses return values + exceptions.
- Don't use `context.GetRequiredService<T>()` — use constructor DI.
- Don't do I/O or HTTP in workflow code (workflows live in `MagicPAI.Server/Workflows/`, not here) — activities ARE where I/O belongs.
- Don't change `MagicPAI.Core` signatures — treat them as fixed.

## After changes

1. `dotnet build MagicPAI.Activities/MagicPAI.Activities.csproj` — 0 errors.
2. `dotnet test MagicPAI.Tests --filter "Category=Unit"` — green.
3. Update `SCORECARD.md` Phase 2 → Activities ported row for your method.
