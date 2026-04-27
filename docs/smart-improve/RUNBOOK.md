# SmartImprove Runbook

Practical guide to running, observing, and tuning a `SmartImprove` session.
Complements `newplan.md` (architecture) and `PATCHES.md` (workflow versioning).

---

## 1. When to use it

`SmartImprove` is the right workflow when you have:

- An existing project (any type — game / web / API / CLI / library / desktop)
- A **vague-ish improvement intent** ("fix what's broken", "add tests until coverage is reasonable", "harden the auth path")
- Trust that **external verification** (build / test / lint / Playwright) is the source of truth, not the model's self-report

It's the wrong workflow when:

- The task is one specific edit → use `SimpleAgent` or `FullOrchestrate`
- There's no executable verification (pure documentation, design discussion) → use `PromptEnhancer` or `ResearchPipeline`
- You want a single fix-burst with no oscillator → use `SmartIterativeLoop` directly

---

## 2. Dispatch — `POST /api/sessions`

Minimum request (all defaults):

```bash
curl -X POST http://localhost:5000/api/sessions \
  -H "Content-Type: application/json" \
  -d '{
    "prompt": "<what you want done>",
    "workflowType": "SmartImprove",
    "aiAssistant": "claude",
    "model": "auto",
    "workspacePath": "C:/path/to/project"
  }'
```

Returns `{ "sessionId": "mpai-…", "workflowType": "SmartImprove" }` on 202 Accepted.

### Tunable knobs (all optional)

| Field | Default | Purpose |
|---|---|---|
| `maxBursts` | 30 | Hard cap on burst count. Lower for tight budgets. |
| `maxTotalIterations` | 200 | Hard cap on cumulative fix iterations across bursts. |
| `maxTotalBudgetUsd` | 50 | Hard $ cap. Workflow exits "budget" if exceeded. |
| `requiredCleanVerifies` | 2 | Consecutive clean verifier cycles required to terminate as `verified-clean`. Lower to 1 for fast smoke tests. |
| `silenceCountdownIterations` | 2 | Iterations of empty filesystem delta after `[DONE]` to confirm completion. |
| `burstSchedule` | `[8, 8, 5, 5, …]` | Override the default schedule. Array; steady-state size used after array exhausted. |
| `steadyStateBurstSize` | 5 | Burst size after the explicit schedule is exhausted. |

### Recommendations by intent

| Intent | Suggested config |
|---|---|
| Fast smoke ("does the wiring work?") | `maxBursts=2, requiredCleanVerifies=1, maxTotalBudgetUsd=3, burstSchedule=[3,2]` |
| Single bug fix | `maxBursts=3, requiredCleanVerifies=2, burstSchedule=[5,3,3]` |
| Open-ended "improve the project" | use defaults |
| High-stakes hardening (security, prod data) | `requiredCleanVerifies=3, maxTotalBudgetUsd=200` |

---

## 3. Observing a live run

### Studio UI — `http://localhost:5000/sessions`

The session and its child workflows appear in the list:
- `<sessionId>` — the parent `SmartImproveWorkflow`
- `<sessionId>-context` — `ContextGathererWorkflow` (preprocess)
- `<sessionId>-burst-1`, `-burst-2`, … — per-burst `SmartIterativeLoopWorkflow`

Click any of them for the live SignalR stream.

### Workflow queries — observable from anywhere

```bash
SESSION=mpai-…
TQ='--namespace magicpai --address temporal:7233'
docker exec mpai-temporal temporal workflow query --workflow-id $SESSION --type Phase $TQ
docker exec mpai-temporal temporal workflow query --workflow-id $SESSION --type ProjectType $TQ
docker exec mpai-temporal temporal workflow query --workflow-id $SESSION --type TotalIterations $TQ
docker exec mpai-temporal temporal workflow query --workflow-id $SESSION --type CompletedBursts $TQ
docker exec mpai-temporal temporal workflow query --workflow-id $SESSION --type StableVerifyStreak $TQ
docker exec mpai-temporal temporal workflow query --workflow-id $SESSION --type TotalCostUsd $TQ
```

### Phase progression (typical)

```
spawning-container          ← container being created (~5-15 s)
preprocess-context          ← ContextGatherer (3 parallel AI calls, 1-3 min)
preprocess-rubric           ← GenerateRubric (1-2 min)
preprocess-harness          ← PlanVerificationHarness (~1 min)
burst-1                     ← per-iteration RunCliAgent calls
verify-1-run-1              ← first separated verifier run
verify-1-run-2              ← second run with clean rebuild + different seed
classify-1                  ← LLM-judge buckets failures real/structural/environmental
burst-2 / verify-2-* / …    ← repeats until exit
cleanup                     ← container destroy
```

### Workspace artifacts (inside the container)

The model writes these into `/workspace/.smartimprove/`:
- `rubric.json` — generated rubric (project type + items)
- `harness.sh` — runnable verification harness

If you bind-mount a host path, these appear on the host too — useful for debugging
("why did it think the project was a CLI?" → cat `rubric.json` and check `projectType`).

---

## 4. Reading the final result

Workflow result JSON shape:

```json
{
  "ExitReason": "verified-clean | no-progress | budget | max-bursts | max-total | cancelled",
  "IterationsRun": 8,
  "BurstsCompleted": 2,
  "TotalCostUsd": 1.06,
  "FinalRubric": {
    "TotalItems": 14,
    "PassedItems": 13,
    "FailedP0": 1,
    "FailedP1": 0,
    "FailedP2": 0,
    "FailedP3": 0
  },
  "RemainingP2P3Items": ["polish-comment", "rename-method"]
}
```

### Interpreting `ExitReason`

| ExitReason | Meaning | Action |
|---|---|---|
| `verified-clean` | Two consecutive cycles both reported zero P0/P1 failures. Done. | Ship. |
| `no-progress` | Multi-signal no-progress detector fired in the burst. | Investigate why model can't progress (look at last burst's modified files + verifier output). |
| `max-bursts` | Hit the burst cap with rubric still failing. | Either increase `maxBursts` and re-dispatch, or accept the partial state. |
| `max-total` | Hit the cumulative iteration cap. | Same as above with `maxTotalIterations`. |
| `budget` | Cost cap exceeded. | Same; or accept. |
| `cancelled` | Operator stop signal. | Manual decision. |
| `no-rubric-items` | Preprocess produced an empty rubric. | The project is too ambiguous; rewrite the prompt with concrete intent or pick a different workflow. |

### Anti-reward-hacking guarantee

`verified-clean` is **only** possible when the external harness reports zero real
P0/P1 failures across two separated runs. The model emitting `[DONE]` repeatedly
is necessary but not sufficient — the verifier is the source of truth. See
newplan.md §4 and the canary tests `ModelClaimsDoneButVerifierDisagrees_*`.

---

## 5. Common pitfalls

### Rubric generates source-pattern greps that fight code improvements

**Symptom:** Workflow exits `max-bursts` with one stubborn P0 like
`grep -qE 'return\s+a\s*\+\s*b' /workspace/Calc.cs`, even though the build and
tests pass. The model has rewritten `Add` as `checked(a + b)` or
`Math.AddInt32` — better behavior but the literal source string changed.

**Fix:** As of iter-5 the rubric prompt explicitly biases toward behavioral
checks (`dotnet test`, `curl`, `playwright test`) and away from `grep` over
source. If you still see this, manually edit `/workspace/.smartimprove/rubric.json`
to replace the grep with a `dotnet test --filter` run, then re-dispatch.

### Container holds DLL locks on Windows

**Symptom:** `MSB3027 / MSB3021` errors when rebuilding the server while it's
running.

**Fix:** Stop the server first.
```bash
netstat -ano | grep ":5000.*LISTENING" | awk '{print $NF}' | xargs taskkill //F //PID
```

### Model touches `tests/` during a fix burst

**Symptom:** `result.TestsTripped == true` after a burst.

**Diagnosis:** The model edited a test file. This isn't always reward-hacking —
sometimes a real bug is in the test, not the source. The workflow doesn't
auto-revert; it surfaces the flag so a human or the LLM-judge can decide.
Look at the burst's `ModifiedFiles` list to see exactly what changed.

### Two consecutive clean verifies are required (the default `RequiredCleanVerifies=2`)

**Symptom:** A simple project that's clearly fixed runs an extra burst+verify
cycle "for nothing."

**Fix:** For tight smoke tests, set `requiredCleanVerifies=1` in the dispatch
request. This is acceptable when you accept the slight risk of a flaky
verifier returning a false-green once.

---

## 6. Cost expectations (rough, Claude Sonnet)

| Project size | Typical cost per run |
|---|---|
| Tiny lib (1 .cs file, 1 test) | $0.50 - $1.50 |
| Small CLI (~10 files) | $1 - $4 |
| Medium API (~50 files, real tests) | $5 - $15 |
| Larger app | depends entirely on iteration count |

The bulk of cost goes to:
1. ContextGatherer's three parallel AI calls (~$0.30 - $1)
2. Each burst iteration's RunCliAgent (~$0.05 - $0.50 per iter, varies wildly)
3. ClassifyFailures between bursts (~$0.05 - $0.15 per cycle if there are failures)

The activities/snapshots/AST-hashes are free (no LLM).

---

## 7. Cancelling a session

```bash
SESSION=mpai-…
curl -X POST "http://localhost:5000/api/sessions/$SESSION/terminate" \
  -H "Content-Type: application/json" \
  -d '{"reason":"operator-cancel"}'
```

Or send the workflow signal:

```bash
docker exec mpai-temporal temporal workflow signal \
  --workflow-id $SESSION --name RequestStopAsync \
  --input '"why-i-stopped"' \
  --namespace magicpai --address temporal:7233
```

Either way, the workflow honors the signal at the top of its next iteration
(does not interrupt an in-flight activity), then runs the `finally` block
that destroys the container.

---

## 8. References

- `newplan.md` — full architecture, anti-reward-hacking guards, test plan
- `PATCHES.md` — workflow versioning patches
- `MagicPAI.Server/Workflows/SmartImproveWorkflow.cs` — the top-level workflow
- `MagicPAI.Server/Workflows/SmartIterativeLoopWorkflow.cs` — per-burst child
- `MagicPAI.Activities/AI/AiActivities.cs` — `GenerateRubricAsync`, `PlanVerificationHarnessAsync`, `ClassifyFailuresAsync`
- `MagicPAI.Activities/SmartImprove/SmartImproveActivities.cs` — `SnapshotFilesystemAsync`, `ComputeAstHashAsync`, `GetGitStateAsync`, `VerifyHarnessAsync`
