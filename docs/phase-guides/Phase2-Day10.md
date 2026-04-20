# Phase 2 — Day 10: Server unification

**Objective:** make `SessionController`, `SessionHub`, etc. use Temporal exclusively
for all workflow types. Elsa runtime still present but not invoked for new
sessions.

**Duration:** ~6 hours.
**Prerequisites:** Day 9 complete (all 15 workflows ported).

---

## Steps

### Step 1: Rewrite WorkflowCatalog

Replace `MagicPAI.Server/Bridge/WorkflowCatalog.cs` with §M.2 implementation.

### Step 2: Rewrite SessionLaunchPlanner

Replace `MagicPAI.Server/Bridge/SessionLaunchPlanner.cs` with §M.3 implementation.

Contains per-workflow typed converters (`AsSimpleAgentInput`,
`AsFullOrchestrateInput`, etc.) — one per workflow.

### Step 3: Rewrite SessionController

Replace the `/api/sessions` POST body with §M.4 — dispatches via Temporal for all
15 workflow types via switch statement.

Also rewrite `Get` and `DELETE` to use `ITemporalClient` handles.

### Step 4: Rewrite SessionHistoryReader

§12.6 — uses `ListWorkflowsAsync` with visibility query instead of EF Core.

### Step 5: Add hosted services

Per §M.1:
- `SearchAttributesInitializer` (registers search attrs on startup).
- `WorkflowCompletionMonitor` (bridges Temporal completion to SignalR).
- `DockerEnforcementValidator` (startup check).

### Step 6: Rewrite SessionHub signals

Replace `ApproveGate`, `RejectGate`, `InjectPrompt`, `CancelSession` with
Temporal signal calls per §J.2.

### Step 7: Add MagicPaiMetrics

`MagicPAI.Server/Services/MagicPaiMetrics.cs` per §16.5 — OpenTelemetry Counter,
Histogram, UpDownCounter.

### Step 8: Delete the temporary TemporalSessionsController

The Day 3 `TemporalSessionsController` at `/api/temporal/sessions` is now
redundant. Delete it; all traffic routes via `SessionController`.

### Step 9: Verify

```powershell
dotnet build
dotnet test
./scripts/dev-up.ps1 -Rebuild

# Smoke test each workflow type
./scripts/smoke-test.ps1 -WorkflowType SimpleAgent
./scripts/smoke-test.ps1 -WorkflowType FullOrchestrate
# ... etc for all 15 types ...
```

### Step 10: Commit

```powershell
git add MagicPAI.Server/Bridge/
git add MagicPAI.Server/Controllers/
git add MagicPAI.Server/Hubs/
git add MagicPAI.Server/Services/
git add MagicPAI.Server/Program.cs
git commit -m "temporal: server unification — SessionController, SessionHub, Catalog use Temporal"
```

---

## Definition of done

- [ ] `SessionController` dispatches all 15 workflow types via Temporal.
- [ ] `SessionHub.ApproveGate/RejectGate/InjectPrompt` use Temporal signals.
- [ ] `SessionHistoryReader` uses `ListWorkflowsAsync`.
- [ ] `SearchAttributesInitializer` registers on startup.
- [ ] `WorkflowCompletionMonitor` publishes completion to SignalR.
- [ ] `DockerEnforcementValidator` blocks startup if misconfigured.
- [ ] `TemporalSessionsController` deleted.
- [ ] Smoke-tested all 15 workflow types.
- [ ] Commit pushed.

## Next

`Phase2-Day11.md` — Studio rewrite: drop Elsa Studio packages, rebuild Blazor UI.
