# Phase 2 — Day 4: AI activities (part 1)

**Objective:** port `TriageAsync`, `ClassifyAsync`, `RouteModelAsync`, `EnhancePromptAsync`
into `AiActivities`. Four methods covering the core prompt pipeline.

**Duration:** ~6 hours.
**Prerequisites:** Phase 1 complete (`v2.0.0-phase1` tagged).

---

## Steps

### Step 1: Expand AI contracts

Add records to `MagicPAI.Activities/Contracts/AiContracts.cs` per `temporal.md` §7.2:
- `TriageInput` / `TriageOutput`
- `ClassifierInput` / `ClassifierOutput`
- `RouteModelInput` / `RouteModelOutput`
- `EnhancePromptInput` / `EnhancePromptOutput`
- `TriageResult` (internal)

### Step 2: Add methods to AiActivities

Per `temporal.md` §I.1 — add to existing `AiActivities.cs`:
- `TriageAsync` (replaces `TriageActivity`)
- `ClassifyAsync` (replaces `ClassifierActivity`)
- `RouteModelAsync` (replaces `ModelRouterActivity`; no container needed)
- `EnhancePromptAsync` (replaces `PromptEnhancementActivity`)

Copy full bodies from Appendix I.1 of temporal.md. Be careful with:
- Container IDs must be required (no implicit default).
- Auth recovery pattern for each method that calls container exec.
- Structured output schema passed to Claude/Codex/Gemini.

### Step 3: Unit tests

Add to `MagicPAI.Tests/Activities/AiActivitiesTests.cs`:
- `TriageAsync_ParsesResponse_ReturnsComplexity`
- `TriageAsync_FallsBack_WhenJsonInvalid`
- `ClassifyAsync_ReturnsTrue_WhenAgentAgrees`
- `RouteModelAsync_SelectsOpusForHighComplexity`
- `EnhancePromptAsync_ReturnsOriginal_WhenParseFails`

Use `ActivityEnvironment` + Moq.

### Step 4: Build + tests

```powershell
dotnet build
./scripts/run-tests.ps1 Unit
```

### Step 5: Commit

```powershell
git add MagicPAI.Activities/Contracts/AiContracts.cs
git add MagicPAI.Activities/AI/AiActivities.cs
git add MagicPAI.Tests/Activities/AiActivitiesTests.cs
git commit -m "temporal: port 4 AI activities (Triage, Classify, RouteModel, EnhancePrompt)"
```

### Step 6: Update SCORECARD

Check off the 4 methods in Phase 2 Activities ported section.

---

## Definition of done

- [ ] 4 AI activity methods implemented and tested.
- [ ] `dotnet build` clean.
- [ ] Unit tests pass.
- [ ] Commit pushed.
- [ ] SCORECARD updated.

## Next

`Phase2-Day5.md` — remaining AI activities (Architect, ResearchPrompt,
ClassifyWebsiteTask, GradeCoverage) + Git/Verify/Blackboard groups.
