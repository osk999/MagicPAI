# Phase 3 — Day 13: Elsa retirement

**Objective:** Remove all Elsa code, packages, and database tables. MagicPAI
becomes 100% Temporal.

**Duration:** ~6 hours.
**Prerequisites:** Phase 2 complete (`v2.0.0-phase2` tagged).

---

## Steps

### Step 1: Backup before destructive changes

```powershell
./scripts/backup.ps1
```

### Step 2: Remove Elsa packages

Per `temporal.md` Appendix A:

**MagicPAI.Server.csproj:**
Remove 11 `Elsa.*` packages.

**MagicPAI.Activities.csproj:**
Remove 3 `Elsa.*` packages.

**MagicPAI.Workflows.csproj:**
Remove 4 `Elsa.*` packages.

**MagicPAI.Studio.csproj:**
Already cleaned in Day 11.

```powershell
dotnet restore
```

Expect many build errors now — Elsa types referenced but packages gone. Fix
next.

### Step 3: Delete Elsa-dependent files

Per `temporal.md` Appendix B.1:

Activities (Elsa-based):
```powershell
Remove-Item MagicPAI.Activities/AI/RunCliAgentActivity.cs
Remove-Item MagicPAI.Activities/AI/AiAssistantActivity.cs
Remove-Item MagicPAI.Activities/AI/TriageActivity.cs
Remove-Item MagicPAI.Activities/AI/ClassifierActivity.cs
Remove-Item MagicPAI.Activities/AI/ModelRouterActivity.cs
Remove-Item MagicPAI.Activities/AI/PromptEnhancementActivity.cs
Remove-Item MagicPAI.Activities/AI/ArchitectActivity.cs
Remove-Item MagicPAI.Activities/AI/ResearchPromptActivity.cs
Remove-Item MagicPAI.Activities/AI/WebsiteTaskClassifierActivity.cs
Remove-Item MagicPAI.Activities/AI/RequirementsCoverageActivity.cs
Remove-Item MagicPAI.Activities/Docker/SpawnContainerActivity.cs
Remove-Item MagicPAI.Activities/Docker/ExecInContainerActivity.cs
Remove-Item MagicPAI.Activities/Docker/StreamFromContainerActivity.cs
Remove-Item MagicPAI.Activities/Docker/DestroyContainerActivity.cs
Remove-Item MagicPAI.Activities/Git/CreateWorktreeActivity.cs
Remove-Item MagicPAI.Activities/Git/MergeWorktreeActivity.cs
Remove-Item MagicPAI.Activities/Git/CleanupWorktreeActivity.cs
Remove-Item MagicPAI.Activities/Verification/RunVerificationActivity.cs
Remove-Item MagicPAI.Activities/Verification/RepairActivity.cs
Remove-Item MagicPAI.Activities/ControlFlow/IterationGateActivity.cs
Remove-Item MagicPAI.Activities/Infrastructure/HumanApprovalActivity.cs
Remove-Item MagicPAI.Activities/Infrastructure/ClaimFileActivity.cs
Remove-Item MagicPAI.Activities/Infrastructure/UpdateCostActivity.cs
Remove-Item MagicPAI.Activities/Infrastructure/EmitOutputChunkActivity.cs
```

Workflow base + helpers:
```powershell
Remove-Item MagicPAI.Server/Workflows/WorkflowBase.cs
Remove-Item MagicPAI.Server/Workflows/WorkflowBuilderVariableExtensions.cs
Remove-Item MagicPAI.Server/Workflows/WorkflowInputHelper.cs
```

Elsa-era workflow files (replaced by Temporal/ subdir):
```powershell
Remove-Item MagicPAI.Server/Workflows/SimpleAgentWorkflow.cs
# ... etc for every workflow that had a Temporal equivalent in Workflows/Temporal/
```

Obsolete workflows:
```powershell
Remove-Item MagicPAI.Server/Workflows/IsComplexAppWorkflow.cs
Remove-Item MagicPAI.Server/Workflows/IsWebsiteProjectWorkflow.cs
Remove-Item MagicPAI.Server/Workflows/LoopVerifierWorkflow.cs
Remove-Item MagicPAI.Server/Workflows/TestSetPromptWorkflow.cs
Remove-Item MagicPAI.Server/Workflows/TestClassifierWorkflow.cs
Remove-Item MagicPAI.Server/Workflows/TestWebsiteClassifierWorkflow.cs
Remove-Item MagicPAI.Server/Workflows/TestPromptEnhancementWorkflow.cs
Remove-Item MagicPAI.Server/Workflows/TestFullFlowWorkflow.cs
```

JSON templates:
```powershell
Remove-Item MagicPAI.Server/Workflows/Templates/*.json
```

Bridge files:
```powershell
Remove-Item MagicPAI.Server/Bridge/ElsaEventBridge.cs
Remove-Item MagicPAI.Server/Bridge/WorkflowPublisher.cs
Remove-Item MagicPAI.Server/Bridge/WorkflowCompletionHandler.cs
Remove-Item MagicPAI.Server/Bridge/WorkflowProgressTracker.cs
```

Providers:
```powershell
Remove-Item MagicPAI.Server/Providers/MagicPaiActivityDescriptorModifier.cs
```

### Step 4: Namespace reorganization

Move Temporal workflows from `Workflows/Temporal/` back to `Workflows/`:
```powershell
Move-Item MagicPAI.Server/Workflows/Temporal/*.cs MagicPAI.Server/Workflows/
Remove-Item MagicPAI.Server/Workflows/Temporal
```

Update namespaces in files: `MagicPAI.Server.Workflows.Temporal` → `MagicPAI.Server.Workflows`.

Update any `Program.cs` / `SessionController.cs` references.

### Step 5: Remove Elsa wiring from Program.cs

Delete the `builder.AddElsa(...)` block entirely along with:
- `app.UseWorkflowsApi("elsa/api")`
- `ElsaEventBridge` registration.
- `WorkflowPublisher` registration.
- `ElsaStudioApiKeyHandler` wiring.
- `MagicPaiActivityDescriptorModifier` registration.

Final `Program.cs` should match §M.1.

### Step 6: Rebuild

```powershell
dotnet restore
dotnet build
```

Fix remaining build errors — usually orphan `using Elsa.*` lines to delete.

Target: zero errors, zero warnings.

### Step 7: Drop Elsa database tables

Create migration `MagicPAI.Server/Migrations/20260430000000_DropElsaSchema.cs`
per §K.2.

```powershell
dotnet ef migrations add DropElsaSchema --project MagicPAI.Server
dotnet ef database update --project MagicPAI.Server
```

Verify:
```powershell
docker exec mpai-db psql -U magicpai -c "\dt"
# Should show only: session_events, cost_tracking, container_registry,
#                   __EFMigrationsHistory
```

### Step 8: Verify no Elsa references remain

```powershell
# grep returns zero lines
Get-ChildItem -Recurse -Include *.cs, *.csproj, *.json |
    Where-Object { $_.FullName -notmatch 'document_refernce_opensource|bin|obj' } |
    Select-String -Pattern 'using Elsa\.|"Elsa\.|\[Activity\]|WorkflowBase' |
    Where-Object { $_.Line -notmatch 'Temporalio|temporal\.md|MIGRATION_PLAN' }
```

Expect: empty output.

### Step 9: Run all tests

```powershell
dotnet test
```

All green. No Elsa tests remain.

### Step 10: Smoke test

```powershell
./scripts/dev-up.ps1 -Rebuild -Clean
./scripts/smoke-test.ps1
```

### Step 11: Commit

```powershell
git add -A
git commit -m "temporal: Phase 3 day 1 — Elsa retired

- Removed all Elsa NuGet packages.
- Deleted ~40 Elsa-dependent files.
- Applied DropElsaSchema migration.
- grep verifies zero Elsa references.
- dotnet build clean; tests green; smoke test passes."
```

### Step 12: Update SCORECARD

Mark Phase 3 code-cleanup section complete.

---

## Definition of done

- [ ] `grep -rE "Elsa\." MagicPAI.*` returns zero hits.
- [ ] Elsa tables dropped (verified in psql).
- [ ] `dotnet build` zero warnings.
- [ ] `dotnet test` all pass.
- [ ] Smoke test passes.
- [ ] SCORECARD updated.

## Next

`Phase3-Day14.md` — final: update CLAUDE.md, MAGICPAI_PLAN.md, reference snapshots,
tag `v2.0.0-temporal`.
