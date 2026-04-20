# Phase 2 — Day 5: Remaining activities (AI part 2 + Git + Verify + Blackboard)

**Objective:** complete the activity layer. After today all 20 methods across 5
classes are in place.

**Duration:** ~7 hours.
**Prerequisites:** Day 4 complete.

---

## Steps

### Step 1: Complete AiActivities

Add the remaining 4 methods to `MagicPAI.Activities/AI/AiActivities.cs` per
`temporal.md` Appendix I.1:
- `ArchitectAsync`
- `ResearchPromptAsync`
- `ClassifyWebsiteTaskAsync`
- `GradeCoverageAsync`

Add corresponding contract records to `AiContracts.cs` per §7.2:
- `ArchitectInput`/`ArchitectOutput` + `TaskPlanEntry`
- `ResearchPromptInput`/`ResearchPromptOutput`
- `WebsiteClassifyInput`/`WebsiteClassifyOutput`
- `CoverageInput`/`CoverageOutput`

### Step 2: Create GitActivities.cs

Per `temporal.md` §I.2:
- `MagicPAI.Activities/Contracts/GitContracts.cs` — 3 input/output record pairs.
- `MagicPAI.Activities/Git/GitActivities.cs` — 3 methods:
  - `CreateWorktreeAsync`
  - `MergeWorktreeAsync`
  - `CleanupWorktreeAsync`

### Step 3: Create VerifyActivities.cs

Per `temporal.md` §I.3:
- `MagicPAI.Activities/Contracts/VerifyContracts.cs`
- `MagicPAI.Activities/Verification/VerifyActivities.cs`:
  - `RunGatesAsync`
  - `GenerateRepairPromptAsync`

### Step 4: Create BlackboardActivities.cs

Per `temporal.md` §I.4:
- `MagicPAI.Activities/Contracts/BlackboardContracts.cs`
- `MagicPAI.Activities/Infrastructure/BlackboardActivities.cs`:
  - `ClaimFileAsync`
  - `ReleaseFileAsync`

### Step 5: Register in DI

Add to `Program.cs`:
```csharp
.AddScopedActivities<AiActivities>()        // already registered; adds new methods automatically
.AddScopedActivities<GitActivities>()       // NEW
.AddScopedActivities<VerifyActivities>()    // NEW
.AddScopedActivities<BlackboardActivities>() // NEW
```

### Step 6: Delete old Elsa activity files

After verifying new activities work, delete the old ones per §B.1:
- `MagicPAI.Activities/AI/TriageActivity.cs`
- `MagicPAI.Activities/AI/ClassifierActivity.cs`
- `MagicPAI.Activities/AI/ModelRouterActivity.cs`
- `MagicPAI.Activities/AI/PromptEnhancementActivity.cs`
- `MagicPAI.Activities/AI/ArchitectActivity.cs`
- `MagicPAI.Activities/AI/ResearchPromptActivity.cs`
- `MagicPAI.Activities/AI/WebsiteTaskClassifierActivity.cs`
- `MagicPAI.Activities/AI/RequirementsCoverageActivity.cs`
- `MagicPAI.Activities/AI/RunCliAgentActivity.cs` (from Day 3 carry-over)
- `MagicPAI.Activities/AI/AiAssistantActivity.cs`
- `MagicPAI.Activities/Git/*.cs` (3 files)
- `MagicPAI.Activities/Verification/*.cs` (2 files)
- `MagicPAI.Activities/Infrastructure/HumanApprovalActivity.cs`
- `MagicPAI.Activities/Infrastructure/ClaimFileActivity.cs`
- `MagicPAI.Activities/Infrastructure/UpdateCostActivity.cs`
- `MagicPAI.Activities/Infrastructure/EmitOutputChunkActivity.cs`
- `MagicPAI.Activities/Docker/*.cs` (4 files; already replaced Day 2)
- `MagicPAI.Activities/ControlFlow/IterationGateActivity.cs`

(Total: ~24 files deleted.)

**Gotcha:** some of these are referenced by existing Elsa workflow classes in
`MagicPAI.Server/Workflows/*.cs`. Those workflows are still coexisting with the
Temporal copies. You'll see build breaks — that's expected. Don't delete the
Elsa activities yet; **revert this step** and wait until Day 11 (server
unification) when we remove all Elsa workflows at once.

**Corrected Step 6:** skip deletions for now. Just verify new Temporal
activities coexist with old Elsa ones by using different file paths:
- Temporal: `DockerActivities.cs` (new consolidated class).
- Elsa: `SpawnContainerActivity.cs` (old, unchanged).

Both can exist until Phase 3.

### Step 7: Tests

Create/update tests for each new method. Template in `temporal.md` §15.3.

Target: 80% coverage on `MagicPAI.Activities`.

### Step 8: Build + test

```powershell
dotnet build
./scripts/run-tests.ps1 Unit
```

### Step 9: Commit

```powershell
git add MagicPAI.Activities/
git add MagicPAI.Tests/Activities/
git add MagicPAI.Server/Program.cs
git commit -m "temporal: port all 20 activity methods across 5 groups"
```

---

## Definition of done

- [ ] `AiActivities` has 9 methods.
- [ ] `GitActivities` has 3 methods.
- [ ] `VerifyActivities` has 2 methods.
- [ ] `BlackboardActivities` has 2 methods.
- [ ] All 5 classes DI-registered.
- [ ] All unit tests pass.
- [ ] Commit pushed.
- [ ] SCORECARD updated.

## Next

`Phase2-Day6.md` — workflow contract records (15 contract files).
