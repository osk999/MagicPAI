# Phase 2 — Day 11: Studio rewrite

**Objective:** Rebuild `MagicPAI.Studio` on MudBlazor only; remove all Elsa Studio
packages and integration code.

**Duration:** ~6 hours.
**Prerequisites:** Day 10 complete (server unified).

---

## Steps

### Step 1: Update MagicPAI.Studio.csproj

Per §A.5 diff:
- Remove 8 `Elsa.Studio.*` + `Elsa.Api.Client` packages.
- Add `MudBlazor` 7.15.0.

```powershell
dotnet restore MagicPAI.Studio
```

### Step 2: Rewrite Program.cs

Per §10.5. Drastically simpler — no Elsa modules, just MudBlazor + HTTP + SignalR client.

### Step 3: Rewrite App.razor, MainLayout, NavMenu

Per §10.6, §10.7.

### Step 4: Create components

8 new components in `MagicPAI.Studio/Components/` per Appendix S:
- `SessionInputForm.razor`
- `CliOutputStream.razor`
- `CostDisplay.razor`
- `GateApprovalPanel.razor`
- `ContainerStatusPanel.razor`
- `VerificationResultsTable.razor`
- `SessionStatusBadge.razor`
- `PipelineStageChip.razor`

### Step 5: Create pages

- `Pages/Home.razor` (§S.8)
- `Pages/SessionList.razor` (§10.12)
- `Pages/SessionView.razor` — **rewrite** (§10.9)
- `Pages/SessionInspect.razor` (§10.11) — iframes Temporal UI
- Keep `Pages/Dashboard.razor`, `CostDashboard.razor`, `Settings.razor` with
  minor updates per §S.9.

### Step 6: Services

- `Services/BackendUrlResolver.cs` — unchanged.
- `Services/SessionApiClient.cs` — rewrite for new types from `docs/openapi.yaml`.
- `Services/SessionHubClient.cs` — rewrite per §J.4.
- `Services/TemporalUiUrlBuilder.cs` — new (§10.10).
- `Services/WorkflowCatalogClient.cs` — new.

Delete:
- `Services/MagicPaiFeature.cs`
- `Services/MagicPaiMenuProvider.cs`
- `Services/MagicPaiMenuGroupProvider.cs`
- `Services/MagicPaiWorkflowInstanceObserverFactory.cs`
- `Services/ElsaStudioApiKeyHandler.cs`
- `Pages/ElsaStudioView.razor`

### Step 7: Update shared hub types

Create `MagicPAI.Shared/Hubs/ISessionHubClient.cs` and `HubPayloads.cs` per §J.1,
§J.3 if not already present.

### Step 8: Build + launch

```powershell
dotnet build MagicPAI.Studio
./scripts/dev-up.ps1 -Rebuild
```

Open `http://localhost:5000`. Verify:
- Home page renders.
- Session creation form shows with workflow dropdown populated.
- Create a SimpleAgent session.
- Stream appears live.
- Cancel button works.
- "View in Temporal UI" deep-links correctly.

### Step 9: Manual UI smoke for all 15 workflow types

This is the Phase 2 capstone. Cycle through each workflow type; verify
end-to-end.

Track in SCORECARD.md:
- [ ] SimpleAgent
- [ ] FullOrchestrate
- [ ] DeepResearchOrchestrate
- [ ] OrchestrateSimplePath
- [ ] OrchestrateComplexPath
- [ ] PromptEnhancer
- [ ] ContextGatherer
- [ ] PromptGrounding
- [ ] PostExecutionPipeline
- [ ] ResearchPipeline
- [ ] StandardOrchestrate
- [ ] ClawEvalAgent
- [ ] WebsiteAuditCore
- [ ] WebsiteAuditLoop
- [ ] VerifyAndRepair

Take screenshots as evidence.

### Step 10: Commit

```powershell
git add MagicPAI.Studio/ MagicPAI.Shared/
git commit -m "temporal: Studio rewritten on MudBlazor; Elsa Studio dropped"
```

---

## Definition of done

- [ ] `MagicPAI.Studio.csproj` has no `Elsa.Studio.*` packages.
- [ ] Studio builds clean.
- [ ] All 15 workflow types bookable + complete via UI.
- [ ] "View in Temporal UI" deep-link works for every session.
- [ ] No broken pages / console errors.
- [ ] Screenshots of each workflow in `docs/phase2-ui-screenshots/` (optional but helpful).

## Next

`Phase2-Day12.md` — test cleanup + Phase 2 exit criteria validation.
