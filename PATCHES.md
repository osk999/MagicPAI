# MagicPAI workflow patches

Every `Workflow.Patched` in the codebase is tracked here. This is the canonical log
for workflow-versioning debt.

**Rule:** every `Workflow.Patched("patch-id")` must have an entry here.

Last updated: 2026-04-20
Active patches: **0** (clean slate at start of migration)

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

*(none at start of Phase 0; future entries added here as workflow changes
require them)*

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
