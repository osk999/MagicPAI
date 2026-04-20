# Phase 2 — Day 12: Test cleanup + Phase 2 exit validation

**Objective:** ensure tests are in a good state; validate Phase 2 exit criteria;
tag `v2.0.0-phase2`.

**Duration:** ~4 hours.
**Prerequisites:** Day 11 complete (Studio rewrite done).

---

## Steps

### Step 1: Test directory reorganization

Reorganize `MagicPAI.Tests/` to match final shape:
- `Activities/` — keep existing unit tests (updated to new method signatures).
- `Workflows/` — new directory for integration + replay tests.
- `Workflows/Histories/` — subdirs per workflow with captured fixtures.
- `Workflows/E2E/` — smoke tests against real Temporal dev server.
- `Server/` — existing controller tests, rewritten.
- `TestInfrastructure/` — shared builders, stubs, fixtures (per Appendix RR).

### Step 2: Delete obsolete tests

Per §B.1:
- `Activities/AiActivityDescriptorTests.cs` (no descriptors anymore).
- `Server/ElsaEventBridgeTests.cs` (bridge gone).

### Step 3: Rewrite legacy tests

Old tests that mocked `ActivityExecutionContext` must be rewritten against
`ActivityEnvironment` or `WorkflowEnvironment`. Affected files typically:
- `RunCliAgentActivityTests.cs` → tests for `AiActivities.RunCliAgentAsync`.
- `TriageActivityTests.cs` → tests for `AiActivities.TriageAsync`.
- `VerificationActivityTests.cs` → tests for `VerifyActivities.RunGatesAsync`.
- `ResearchPromptActivityTests.cs` → tests for `AiActivities.ResearchPromptAsync`.
- `SpawnContainerSmokeTests.cs` → tests for `DockerActivities.SpawnAsync`.

Keep:
- `Activities/AssistantSessionStateTests.cs` (utility test).
- `Activities/ContainerLifecycleSmokeTests.cs` (still valid).
- `Server/BrowseControllerTests.cs` (unchanged).
- `Server/SessionHistoryReaderTests.cs` (rewrite for Temporal-based reader).
- `Server/SessionLaunchPlannerTests.cs` (rewrite for new planner).

### Step 4: Verify coverage targets

```powershell
./scripts/run-tests.ps1 All -Coverage
reportgenerator -reports:"./test-results/**/coverage.cobertura.xml" -targetdir:"./coverage-report"
```

Open `coverage-report/index.html`. Target per §15.9:
- MagicPAI.Activities — > 80% line coverage.
- MagicPAI.Workflows — 100% of classes have ≥ 1 test.

### Step 5: Verify replay fixtures

```powershell
./scripts/run-tests.ps1 Replay
```

Every workflow must replay cleanly.

Cross-check: every `*.cs` in `MagicPAI.Server/Workflows/Temporal/` has a
corresponding `Histories/*/` directory with at least one fixture.

### Step 6: Run full E2E smoke

```powershell
./scripts/dev-up.ps1 -Rebuild
for ($w in "SimpleAgent", "FullOrchestrate", "StandardOrchestrate") {
    ./scripts/smoke-test.ps1 -WorkflowType $w
}
```

All must pass.

### Step 7: Final Phase 2 exit validation

Go through SCORECARD.md Phase 2 checkboxes. Every item must be checked.

Manual verification:
- [ ] `SessionController` uses Temporal only.
- [ ] Studio rebuilt, no Elsa Studio packages.
- [ ] All 15 workflows bookable via UI.
- [ ] No orphaned containers after runs.
- [ ] Replay tests pass.

### Step 8: Tag

```powershell
git tag v2.0.0-phase2
git push origin v2.0.0-phase2   # only if approved by release mgr
```

### Step 9: Sign-off meeting

Gather tech lead, release manager, ops lead. Demo:
- Create 3 sessions of different types.
- Watch them stream.
- Cancel one.
- Show Temporal UI event history.

Sign off in SCORECARD.md Phase 2 section.

---

## Definition of done (Phase 2 exit)

- [ ] All 15 workflows tested (unit + integration + replay).
- [ ] 80% coverage on Activities project.
- [ ] Manual UI smoke for every workflow type.
- [ ] Tag `v2.0.0-phase2` created.
- [ ] Sign-off recorded.

## Next

`Phase3-Day13.md` — Elsa retirement: remove packages, drop DB tables, delete obsolete files.
