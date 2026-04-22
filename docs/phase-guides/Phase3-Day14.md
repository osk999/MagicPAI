# Phase 3 — Day 14: Final sign-off

**Objective:** Update docs, reference snapshots, tag `v2.0.0-temporal`. Migration
complete.

**Duration:** ~4 hours.
**Prerequisites:** Day 13 complete (Elsa code removed).

---

## Steps

### Step 1: Update CLAUDE.md

Replace sections per `temporal.md` Appendix R:
- Remove: "Elsa Activity Rules", "Elsa JSON vs C# Workflow Rules",
  "Elsa Variable Shadowing Bug".
- Add: "Temporal Workflow Rules" (non-determinism, activities, signals,
  profiles, versioning).
- Update "Stack" line.
- Update "Solution Structure" table.
- Update "Open Source Reference Policy" to point to Temporal docs.
- Update "E2E Workflow Verification via UI".

Commit:
```powershell
git add CLAUDE.md
git commit -m "temporal: update CLAUDE.md for Temporal-only stack"
```

Verify grep:
```powershell
Get-Content CLAUDE.md | Select-String -Pattern 'Elsa|WorkflowBase|FlowNode|UIHint|Input<|Output<'
# Should be empty (or only in historical context sections)
```

### Step 2: Update MAGICPAI_PLAN.md

Find and replace architecture references:
- "Elsa Workflows 3.6" → "Temporal.io 1.13"
- Workflow designer references → code-only workflows.
- File manifest: reflect new structure.

Commit:
```powershell
git add MAGICPAI_PLAN.md
git commit -m "temporal: update MAGICPAI_PLAN.md architecture references"
```

### Step 3: Update README.md

- Stack: Temporal.io 1.13.
- Quickstart: point at `./scripts/dev-up.ps1` + `./scripts/smoke-test.ps1`.
- Documentation section: add `temporal.md`, `SCORECARD.md`, `docs/phase-guides/`.

Commit.

### Step 4: Remove Elsa reference snapshots

```powershell
Remove-Item -Recurse document_refernce_opensource/elsa-core
Remove-Item -Recurse document_refernce_opensource/elsa-studio
```

### Step 5: Add Temporal reference snapshots

Option A — git submodule:
```powershell
git submodule add https://github.com/temporalio/sdk-dotnet.git document_refernce_opensource/temporalio-sdk-dotnet
git submodule add https://github.com/temporalio/documentation.git document_refernce_opensource/temporalio-docs
```

Option B — snapshot (preferred for offline use):
```powershell
New-Item -ItemType Directory -Force document_refernce_opensource/temporalio-sdk-dotnet
Copy-Item -Recurse <path-to-cloned-sdk-dotnet>/* document_refernce_opensource/temporalio-sdk-dotnet/
```

### Step 6: Update reference index

Edit `document_refernce_opensource/README.md` and `REFERENCE_INDEX.md`:
- Remove Elsa entries.
- Add Temporal entries with links to key docs.

Commit:
```powershell
git add document_refernce_opensource/
git commit -m "temporal: reference snapshots updated — Temporal in, Elsa out"
```

### Step 7: Update memory files (Claude Code)

Per `temporal.md` §OO.1:
- Add `memory/project_temporal_active.md`.
- Update `memory/feedback_elsa_variable_shadowing.md` to "RESOLVED via migration".
- Update `memory/MEMORY.md` index.

(Requires access to user's `.claude/projects/` memory directory.)

### Step 8: CI verification

```powershell
git push origin temporal
# Wait for CI on GitHub: all jobs green
```

### Step 9: Final verification

Run the full verification checklist from `temporal.md` Appendix DD.4:

```powershell
# Code cleanup
grep -rE "Elsa\." MagicPAI.Core/
grep -rE "Elsa\." MagicPAI.Activities/
grep -rE "Elsa\." MagicPAI.Workflows/
grep -rE "Elsa\." MagicPAI.Server/
grep -rE "Elsa\." MagicPAI.Studio/
grep -rE "Elsa\." MagicPAI.Tests/
# All must be empty.

# Build
dotnet build
# Zero warnings.

# Tests
dotnet test
# All pass.

# Docker
./scripts/dev-up.ps1 -Rebuild -Clean
# All services healthy.

# Smoke test
./scripts/smoke-test.ps1
# Success.

# UI verification
Start-Process http://localhost:5000
# Create sessions of all 15 types via UI; verify each completes.
```

### Step 10: Tag and release

```powershell
git tag v2.0.0-temporal -m "Phase 3 complete; Elsa retired; MagicPAI now runs on Temporal.io"
git push origin v2.0.0-temporal
```

### Step 11: Update CHANGELOG.md

Move "Unreleased" items to new `## [2.0.0] — 2026-04-XX` section.

```powershell
git add CHANGELOG.md
git commit -m "temporal: CHANGELOG 2.0.0 release"
```

### Step 12: Sign-off

Complete SCORECARD.md Phase 3 section. Signatures from tech lead + ops lead.

### Step 13: Schedule retrospective

Per `temporal.md` §DD.6 — within 1 week of Phase 3 completion.

Create `docs/retro-temporal-migration.md` from template.

### Step 14: Team announcement

Use `temporal.md` §MM.3 template.

Post to `#magicpai-eng`, email team, update status page.

### Step 15: Monitor for 48 hours

Watch for:
- Elevated error rates.
- Orphaned containers.
- Unexpected failures in specific workflow types.

If issues arise: troubleshoot via `temporal.md` Appendix W (error glossary) or
§23 (rollback) if critical.

---

## Definition of done (Migration complete!)

- [ ] All Phase 3 SCORECARD checkboxes filled.
- [ ] `CLAUDE.md` Elsa-free.
- [ ] `MAGICPAI_PLAN.md` updated.
- [ ] `document_refernce_opensource/temporalio-*` in place.
- [ ] Tag `v2.0.0-temporal` created and pushed.
- [ ] CI green on `temporal` branch.
- [ ] 48-hour monitoring period started.
- [ ] Team notified.
- [ ] Retrospective scheduled.
- [ ] Sign-offs recorded.

## After migration

- Monitor for 48h (Day 14 + 2).
- Retrospective meeting (Day 14 + 7).
- `memory/` updates to reflect Temporal-only reality.
- Plan Q2 quarterly review (Appendix Q.1).
- Celebrate. 🎉

## Post-migration maintenance

See `temporal.md` Appendix Q — monthly/quarterly/annual checklists.

## Links for the future

- Operations: `temporal.md` §19 + Appendix V (CLI cookbook).
- Adding workflows: `temporal.md` §26.12.
- Upgrading: `temporal.md` Appendix II.
- Incidents: `temporal.md` Appendix AAA (IC) + Appendix LL (DR).
