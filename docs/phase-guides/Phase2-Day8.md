# Phase 2 — Day 8: Core orchestration workflows

**Objective:** port 4 core orchestration workflows. These are more complex;
coverage loops, parallel children, file claiming.

**Duration:** ~6 hours.
**Prerequisites:** Day 7 complete.

---

## Workflows to port

1. **`SimpleAgentWorkflow`** — finalize from Day 3 stub. Add coverage loop.
2. **`OrchestrateSimplePathWorkflow`** — delegates to SimpleAgent. §H.5.
3. **`ComplexTaskWorkerWorkflow`** — child with file claiming. §H.6.
4. **`OrchestrateComplexPathWorkflow`** — fan-out over ComplexTaskWorker. §8.5.

---

## Steps

### Step 1: SimpleAgentWorkflow finalization

Replace Day 3 stub with full §8.4 implementation including coverage loop.

Key additions:
- Coverage loop up to `MaxCoverageIterations` (default 3).
- `[WorkflowQuery]` for `TotalCostUsd` and `CoverageIteration`.
- Verification via `VerifyActivities.RunGatesAsync`.
- Re-verification after each repair pass.

Tests: add `LoopsCoverage_UntilAllMet` case. Add replay fixture for
coverage-loop scenario (`simple-agent/coverage-loop-v1.json`).

### Step 2: OrchestrateSimplePathWorkflow

§H.5. Thin wrapper calling `SimpleAgentWorkflow` as child.

### Step 3: ComplexTaskWorkerWorkflow

§H.6. Key features:
- `ClaimFileAsync` for each file in task.
- Retry claim after 30s if already claimed.
- `ReleaseFileAsync` in finally.

### Step 4: OrchestrateComplexPathWorkflow

§8.5. Key features:
- `ArchitectAsync` to decompose prompt.
- `StartChildWorkflowAsync` fan-out (N children).
- `WhenAllAsync` to await all.
- `CancelAllTasksAsync` signal to abort.

Replay fixture: generate 3-task scenario (`orchestrate-complex/3-tasks-v1.json`).

### Step 5: Build + test

```powershell
dotnet build
./scripts/run-tests.ps1 Unit
./scripts/run-tests.ps1 Integration
./scripts/run-tests.ps1 Replay
```

All must pass.

### Step 6: Commits

Separate commit per workflow; see Day 7 pattern.

---

## Definition of done

- [ ] 4 workflows ported, tested, fixtures captured.
- [ ] SimpleAgent coverage loop works.
- [ ] OrchestrateComplexPath fan-out to N parallel workers.
- [ ] SCORECARD updated.

## Next

`Phase2-Day9.md` — remaining workflows (FullOrchestrate, DeepResearchOrchestrate,
PostExecutionPipeline, ResearchPipeline, StandardOrchestrate, ClawEvalAgent,
WebsiteAuditCore, WebsiteAuditLoop).
