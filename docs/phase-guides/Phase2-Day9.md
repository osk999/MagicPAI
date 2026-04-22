# Phase 2 — Day 9: Remaining workflows

**Objective:** port the 8 remaining workflows. Most are small and composition-based.

**Duration:** ~7 hours.
**Prerequisites:** Day 8 complete (core orchestration workflows ported).

---

## Workflows to port

### Utilities (4)

1. **`PostExecutionPipelineWorkflow`** — §H.7.
2. **`ResearchPipelineWorkflow`** — §H.8.
3. **`StandardOrchestrateWorkflow`** — §H.9.
4. **`ClawEvalAgentWorkflow`** — §H.10.

### Website (2)

5. **`WebsiteAuditCoreWorkflow`** — §H.11. Single-section audit.
6. **`WebsiteAuditLoopWorkflow`** — §H.12. Iterates over sections with
   `SkipRemainingSectionsAsync` signal.

### Central orchestrators (2)

7. **`FullOrchestrateWorkflow`** — §8.6. Most complex; multiple signals, queries.
8. **`DeepResearchOrchestrateWorkflow`** — §H.13. Research pipeline → StandardOrchestrate.

---

## Steps

For each workflow, follow the Day 7 template:
1. Create file under `Workflows/Temporal/`.
2. Register in `Program.cs`.
3. Write integration test.
4. Capture replay fixture.
5. Write replay test.
6. Commit.

Estimated ~45 min per workflow.

### Special considerations

**FullOrchestrateWorkflow** is the heaviest:
- Three signals: `ApproveGateAsync`, `RejectGateAsync`, `InjectPromptAsync`.
- Queries: `PipelineStage`, `TotalCostUsd`.
- Branches: website path vs simple path vs complex path.
- Fixtures: at least 3 scenarios (simple, complex, website).

**WebsiteAuditLoopWorkflow** has signal-based early termination:
- Ensure `SkipRemainingSectionsAsync` is tested.
- Fixture: 5-section complete path + 5-section early-skipped path.

**DeepResearchOrchestrate** chains two child workflows:
- Uses strongest model (Opus) — important for model routing verification.

---

## After all 8 ported

All 15 workflows exist. Run:
```powershell
dotnet test --filter "Category=Integration"
dotnet test --filter "Category=Replay"
```

Fix any failures before moving on.

---

## Definition of done

- [ ] 8 workflow classes added.
- [ ] 15 total workflow classes now exist.
- [ ] All integration tests pass.
- [ ] All replay tests pass.
- [ ] Every workflow has at least 1 replay fixture.
- [ ] All registered in `Program.cs`.
- [ ] SCORECARD updated (all 15 workflows checked off).

## Next

`Phase2-Day10.md` — server unification: `SessionController` uses Temporal
exclusively; `SessionHub` uses Temporal signals.
