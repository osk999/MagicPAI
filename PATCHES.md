# MagicPAI workflow patches

Every `Workflow.Patched` in the codebase is tracked here. This is the canonical log
for workflow-versioning debt.

**Rule:** every `Workflow.Patched("patch-id")` must have an entry here.

Last updated: 2026-04-29
Active patches: **7** (Phase 2 wire-up — stage emission, cost broadcast,
verify-and-repair handoff, worktree-per-task, worktree merge,
DAG-ordered fan-out, and the existing-but-undocumented FullOrchestrate
container handoff)

---

## Format

```markdown
## <patch-id>

- **Workflow:** which `[Workflow]` class
- **Introduced:** YYYY-MM-DD (commit SHA)
- **Purpose:** why this patch exists; what code path differs
- **Old behavior:** what runs for workflows started before this patch
- **New behavior:** what runs for workflows started after this patch
- **Deprecated:** YYYY-MM-DD (commit SHA) — when `DeprecatePatch` applied
- **Removed:** YYYY-MM-DD (commit SHA) — when `if` wrapper fully removed
- **Owner:** [team / individual]
```

---

## Active patches

### full-orchestrate-container-handoff-v1

- **Workflow:** `FullOrchestrateWorkflow`
- **Introduced:** 2026-04-26 (existed pre-Phase-2; documented in Phase 2)
- **Purpose:** When the caller (e.g. `SmartImproveWorkflow`) supplies
  `FullOrchestrateInput.ExistingContainerId`, the workflow reuses that
  container instead of spawning its own. The patch gate skips the new
  branching logic for old histories that always emitted a `SpawnAsync`
  activity at workflow entry.
- **Old behavior:** Always spawn → run → destroy a workflow-owned container.
- **New behavior:** When `ExistingContainerId` is non-null, skip spawn/destroy
  and reuse the parent's container; otherwise fall back to old behavior.
- **Owner:** server agent

### emit-stage-activity-v1

- **Workflow:** `FullOrchestrateWorkflow`, `OrchestrateComplexPathWorkflow`,
  `ComplexTaskWorkerWorkflow`, `VerifyAndRepairWorkflow`,
  `StandardOrchestrateWorkflow`, `SmartImproveWorkflow`
- **Introduced:** 2026-04-29
- **Purpose:** Replace the silent `_pipelineStage = "..."` field-only
  approach with an actual side-channel SignalR emit so the Studio stage
  chip moves through real stages instead of staying at the field default.
  Calls `StageActivities.EmitStageAsync` at every stage boundary.
- **Old behavior:** Field-only; query polled but Studio doesn't poll, so
  chip stuck at "initializing".
- **New behavior:** New `EmitStageAsync` activity scheduled at every
  pipeline-stage transition; sink failures swallowed (UX-only emission).
- **Replay-safe:** old histories never scheduled this activity, so the
  guard returns false during replay and the new path is skipped.
- **Owner:** server agent

### emit-cost-activity-v1

- **Workflow:** `FullOrchestrateWorkflow`, `OrchestrateComplexPathWorkflow`,
  `VerifyAndRepairWorkflow`, `StandardOrchestrateWorkflow`,
  `SmartImproveWorkflow`
- **Introduced:** 2026-04-29
- **Purpose:** Broadcast running total cost mid-session so the Studio
  cost tile updates live instead of only at completion. Pairs with a
  new `StageActivities.EmitCostAsync` activity. New `TotalCostUsd`
  workflow queries were also added on the workflows that previously
  lacked them (`OrchestrateSimplePathWorkflow`, `WebsiteAuditLoopWorkflow`,
  `WebsiteAuditCoreWorkflow`, `DeepResearchOrchestrateWorkflow`,
  `OrchestrateComplexPathWorkflow`, `VerifyAndRepairWorkflow`).
- **Old behavior:** No mid-run cost emission; `WorkflowCompletionMonitor`
  queries `TotalCostUsd` only at completion.
- **New behavior:** Each `_totalCost +=` is followed by a guarded
  `StageActivities.EmitCostAsync` activity invocation.
- **Replay-safe:** old histories never scheduled this activity; gate
  returns false during replay.
- **Owner:** server agent

### full-orchestrate-verify-and-repair-v1

- **Workflow:** `FullOrchestrateWorkflow`
- **Introduced:** 2026-04-29
- **Purpose:** After the existing GradeCoverage-only post-execution loop
  converges on the complex branch, hand off to the reusable
  `VerifyAndRepairWorkflow` child so build/test/lint gates also drive
  repair iterations (not just requirements coverage). Default gates
  `["compile", "test", "lint"]`; `coverage` is intentionally omitted
  (already handled by the existing coverage loop).
- **Old behavior:** Coverage loop only.
- **New behavior:** Coverage loop, then a child `VerifyAndRepairWorkflow`
  invocation against the same container.
- **Replay-safe:** old histories never executed the child workflow start
  command; gate returns false during replay.
- **Owner:** server agent

### complex-path-worktree-v1

- **Workflow:** `OrchestrateComplexPathWorkflow`
- **Introduced:** 2026-04-29
- **Purpose:** Create a per-task git worktree before starting each
  `ComplexTaskWorkerWorkflow` child so parallel children stop racing on
  the bind-mounted filesystem. Each child receives its own
  `WorkspacePath` pointing at `/workspaces/worktrees/task/{TaskId}`.
- **Old behavior:** All children share the parent's `WorkspacePath`.
- **New behavior:** A `GitActivities.CreateWorktreeAsync` activity runs
  per task before child startup; child input gets the per-task worktree
  path.
- **Replay-safe:** old histories never scheduled `CreateWorktree` at this
  point; gate returns false during replay.
- **Owner:** server agent

### complex-path-worktree-merge-v1

- **Workflow:** `OrchestrateComplexPathWorkflow`
- **Introduced:** 2026-04-29
- **Purpose:** After all children finish, merge each per-task branch
  back into the base branch and clean up the worktree. Merge conflicts
  emit a `merge-conflict-{taskId}` stage but DO NOT throw — the
  `verify-and-repair` loop in the parent picks the issue up.
- **Old behavior:** No merge step.
- **New behavior:** `GitActivities.MergeWorktreeAsync` +
  `GitActivities.CleanupWorktreeAsync` per task after fan-out.
- **Replay-safe:** old histories never scheduled these activities; gate
  returns false during replay.
- **Owner:** server agent

### complex-path-dag-ordering-v1

- **Workflow:** `OrchestrateComplexPathWorkflow`
- **Introduced:** 2026-04-29
- **Purpose:** Respect each task's `DependsOn` list when starting child
  workflows. Caps concurrent children at
  `OrchestrateComplexInput.MaxConcurrentWorkers` (default 5, mirrors
  `MagicPaiConfig.MaxConcurrentContainers`). Ready tasks are started up
  to the cap; on any child completion the parent re-evaluates which
  pending tasks are now startable.
- **Old behavior:** Fan-out all children up front; ignore `DependsOn`.
- **New behavior:** DAG-ordered fan-out with concurrency cap.
- **Replay-safe:** old histories captured the fan-out-all schedule, so
  the gate returns false during replay and the legacy branch reproduces
  the original `StartChildWorkflow` ordering.
- **Owner:** server agent

### Post-migration note (2026-04-20)

The fixes applied during the post-migration verification sweep (Fixes #1-#140
in `SCORECARD.md`) changed several workflow shapes:

- **FullOrchestrate** — added post-exec coverage loop on complex branch
  (Fix #3); added HITL `WaitConditionAsync` gate between triage and branch
  selection (Fix #5).
- **ComplexTaskWorker** — added per-subtask `RunGates` + 1-iteration repair
  loop (Fix #4).
- **ContextGatherer** — replaced single `ResearchPromptAsync` with 3-way
  parallel fan-out via `Workflow.WhenAllAsync` (Fix #6).
- **SimpleAgent / OrchestrateComplexPath / PromptEnhancer / PromptGrounding /
  ResearchPipeline / ClawEvalAgent / PostExecutionPipeline / WebsiteAuditCore /
  VerifyAndRepair** — added conditional own-container spawn/destroy when
  `ContainerId` is empty (Fixes #2, #125, #126).

These changes did **NOT** use `Workflow.Patched("patch-id")` because they are
pre-deployment. There are no in-flight workflows started against the old
shape. Evidence:
- All 17 replay fixtures were regenerated against the new shapes and
  `MagicPAI.Tests/Workflows/*ReplayTests.cs` pass with current code
  (17/17 green under `Category=Replay`).
- E2E verification uses freshly-dispatched workflows throughout.

**When future shape changes DO need patching.** If a deployment has in-flight
workflows and a new change alters the activity sequence, wrap the new path in
`Workflow.Patched("my-change-id-v1")` and add an entry above following the
Format template. Candidates for future patches include additional activity
calls inserted into an existing workflow's hot path, reordering of activity
calls, timeout changes on an already-scheduled activity, and purely additive
end-of-workflow steps (replay compares the full event sequence).

---

## Deprecated but not-yet-removed patches

*(no entries; delete once drain window passes)*

---

## Lifecycle cheat sheet

Stage 1 — **Introduced**:
```csharp
if (Workflow.Patched("full-orchestrate-post-pipeline-v1"))
{
    await RunNewPathAsync();
}
else
{
    await RunOldPathAsync();
}
```
- Old workflows: take `else` branch.
- New workflows: take `if` branch.
- Log entry: "Introduced"; `Deprecated` and `Removed` blank.

Stage 2 — **Deprecated** (after ≥ 2× retention window of drain):
```csharp
if (Workflow.DeprecatePatch("full-orchestrate-post-pipeline-v1"))
{
    await RunNewPathAsync();
}
else
{
    // dead code; will panic if any old workflow somehow replays here
    throw new InvalidOperationException("Old path deprecated");
}
```
- All workflows: take `if` branch (asserted).
- Old workflows still in history but drained from running set.
- Log entry: "Deprecated" filled.

Stage 3 — **Removed** (after another retention window):
```csharp
await RunNewPathAsync();        // unconditional
```
- Patch gone; code simplified.
- Log entry: "Removed" filled. Entry moves to "Archived patches" below.

---

## Archived patches

*(patches fully removed from code; kept here for historical reference)*

*(no entries yet)*

---

## Policy

- **Minimum 10 days** between Introduced and Deprecated (2× default 7-day retention).
- **Minimum 10 days** between Deprecated and Removed.
- **Maximum 30 days** for a patch to sit in any stage — either move it forward or
  ADR why we're keeping it longer.
- **Rare to have more than 3 active patches** in a single workflow — if we do, it's
  a refactor signal.

---

## Audit

Quarterly review checklist:
- [ ] Every active patch has a documented owner.
- [ ] Every active patch is < 90 days old, or has ADR extension.
- [ ] Deprecated patches have Removed date planned.
- [ ] Archived patches removed from code (not just from this doc).

Run `grep -rn 'Workflow.Patched\|Workflow.DeprecatePatch' MagicPAI.*/` to cross-check
code against this doc.

---

## Related docs

- `temporal.md` §20 — Versioning strategy.
- `temporal.md` Appendix N — ADRs (see ADR-010 for decision to use Patched over
  Worker Versioning).
