# FullOrchestrateWorkflow Contract

This document defines the non-negotiable behavior, QA checklist, and operator expectations for `FullOrchestrateWorkflow`.

It exists to prevent silent regressions in orchestration, verification, repair loops, observability, and user feedback.

## Scope

This contract applies to:

- `FullOrchestrateWorkflow`
- any extracted subworkflow or reusable component used by that workflow
- the server-side tracking and event bridge that expose workflow progress
- the Studio session view that presents workflow activity and outputs

## Primary Goal

`FullOrchestrateWorkflow` must reliably choose the correct execution path, produce visible progress, verify results before success, and fail loudly when it cannot safely continue.

## Must Never Break

1. `triage` must produce a visible verdict or fail visibly.
2. A `Simple` verdict must route to the simple path only.
3. A `Complex` verdict must route through architecture and verification.
4. Classifier output must affect execution, not just logging.
5. Architect output must affect the complex-agent prompt, not just logging.
6. Final success must only happen after verification.
7. Failed verification must enter the repair loop until it passes, becomes inconclusive, or reaches the configured attempt limit.
8. No internal activity failure may silently degrade into the wrong path without clear logs and UI-visible evidence.
9. Container lifecycle must remain balanced: spawn once, destroy once, and clean up on terminal completion.
10. Session state, activity history, and outputs shown to the user must match backend truth.
11. A late-opening session page must still recover activity history, outputs, insights, and final state.
12. If the workflow becomes slow or blocked, operators must be able to tell whether it is still producing recent activity or is likely stuck.

## Required Path Semantics

### Simple Path

Expected shape:

`spawn -> triage -> simple-agent -> simple-verify -> destroy`

Required behavior:

- the selected model is resolved before agent execution
- the prompt sent to the agent is the final prompt actually used
- verification result is recorded
- success is impossible if verification did not run

### Complex Path

Expected shape:

`spawn -> triage -> architect -> complex-agent -> complex-verify -> complex-repair -> repair-agent -> complex-verify -> destroy`

Required behavior:

- the architect produces a task breakdown or explicit structured guidance
- the complex-agent prompt incorporates that architect output
- each repair attempt is observable
- the loop exits only on pass, inconclusive result, or retry exhaustion

## Required Observability

The system must expose enough data for QA and operators to understand what the workflow is doing without reading internal code.

### Activity Visibility

The user must be able to see:

- current workflow status
- current or last activity name
- activity history
- last output timestamp
- last activity timestamp
- whether work is still active or has gone quiet

### Insight Visibility

The user must be able to inspect:

- classifier result and verdict
- recommended model or power if classifier produced one
- architect result for complex work
- prompt enhancement before and after, with verdict
- repair prompt details for each retry

### Stuck Detection

If the workflow takes a long time, the UI and API must make it possible to distinguish between:

- still running and producing recent output
- still running but only producing activity updates
- stalled with no recent activity or output

## Failure Handling Rules

1. If `triage` fails, the failure must be discoverable from logs and workflow state.
2. If `architect` fails on the complex path, the workflow must not quietly pretend architecture was unnecessary.
3. If verification fails, the repair path must be entered or the workflow must fail explicitly.
4. If repair generation fails, that failure must be visible and terminal behavior must remain correct.
5. If cleanup fails, container identifiers and destroy attempts must remain traceable.

## Reuse Rules

If a section of `FullOrchestrateWorkflow` is extracted into a subworkflow or reusable component:

1. behavior must remain identical from the caller's perspective
2. observability must not regress
3. retries and terminal conditions must remain externally visible
4. integration tests must cover both the reusable component and the parent workflow path that uses it

## QA Checklist

Use this checklist for every significant change to `FullOrchestrateWorkflow` or any of its components.

1. `triage` runs and records a visible verdict.
2. `Simple` and `Complex` verdicts both route to the correct path.
3. classifier output changes downstream execution when applicable.
4. architect output changes the complex-agent prompt when applicable.
5. the simple path completes with spawn, agent run, verification, and destroy.
6. the complex path completes with architecture, agent run, verification, repair loop as needed, and destroy.
7. the repair loop stops correctly on pass, inconclusive result, and max attempts.
8. no silent fallback occurs when triage, architect, verify, or repair encounters an error.
9. session logs and outputs appear in real time while the workflow is running.
10. prompt enhancement data is visible with before, after, and verdict.
11. classifier result is visible with enough detail for QA to judge correctness.
12. container cleanup happens on success, failure, cancel, and unexpected terminal completion.
13. the session page can be opened late and still reconstruct state from backend data.
14. the published UI renders the same activity truth returned by the API.

## Manual Operator Checks

When a run appears slow, incomplete, or suspicious:

1. check whether `LastOutputAt` is recent
2. check whether `LastActivityAt` is recent
3. compare session status with activity history
4. inspect classifier, architect, prompt-transform, and repair insights
5. confirm whether verification is still pending, retrying, or exhausted
6. confirm whether a container was spawned and later destroyed

Operator verdict guidance:

- recent output means the workflow is active
- recent activity without output may still be healthy, depending on the current step
- no recent output and no recent activity suggests the workflow is stuck or a tracking path is broken
- missing insights for classifier or prompt enhancement suggest observability regression even if execution succeeded

## Minimum Test Coverage

At minimum, automated coverage should verify:

- simple `full-orchestrate` execution
- complex `full-orchestrate` execution
- verification failure followed by successful repair
- repair retry exhaustion behavior
- prompt-transform insight capture
- classifier insight capture
- architect insight capture
- container cleanup on terminal completion
- session API recovery of activities, outputs, and insights for a late-joining UI

Manual browser QA should verify:

- Dashboard can start a `full-orchestrate` session
- Session page shows live updates
- Session page shows activity history
- Session page shows insights
- Elsa Studio remains reachable for workflow inspection

## Runbook

Common local commands:

```powershell
dotnet test MagicPAI.Tests\MagicPAI.Tests.csproj --no-restore /p:UseSharedCompilation=false
dotnet test MagicPAI.Tests.UI\MagicPAI.Tests.UI.csproj --no-restore /p:UseSharedCompilation=false
dotnet test MagicPAI.Tests.Integration\MagicPAI.Tests.Integration.csproj --no-restore /p:UseSharedCompilation=false
docker compose -f docker/docker-compose.test.yml up -d --build --wait
```

Useful local URLs:

- Elsa Studio: `http://localhost:5099/`
- MagicPAI Dashboard: `http://localhost:5099/magic/dashboard`

## Change Rule

Any change to `FullOrchestrateWorkflow` or its extracted components is incomplete unless this contract still holds in both automated tests and browser-visible behavior.
