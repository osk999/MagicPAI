# Workflow loop audit — use of `IterativeLoopWorkflow`

Date: 2026-04-21

Scope: every `while` / `for` / `foreach` loop and every "run agent N times"
pattern in `MagicPAI.Server/Workflows/**`. The goal was to surface every
loop and decide whether it should be replaced by the new
`IterativeLoopWorkflow` or kept as bespoke control flow.

## Converted ✅

| Workflow | Loop | Change |
|----------|------|--------|
| `ResearchPipelineWorkflow` | single `ResearchPromptAsync` call → now drives `IterativeLoopWorkflow` (MinIter=3, MaxIter=20) as a child | `ResearchPipelineWorkflow.cs` |
| `FullOrchestrateWorkflow` | research stage was calling `ResearchPromptAsync` directly → now dispatches `ResearchPipelineWorkflow` (which is iterative) as a child | `FullOrchestrateWorkflow.cs:155-183` |
| `DeepResearchOrchestrateWorkflow` | already dispatches `ResearchPipelineWorkflow` — inherits the iterative behavior automatically | no change |

Every entry point that does "research before execution" now runs the
multi-pass research loop (3-pass protocol, 12 structured-progress tasks,
up to 20 iterations, up to $4 budget cap).

## Deliberately NOT converted — bespoke loops with different semantics

| Workflow | Loop type | Why `IterativeLoopWorkflow` isn't a fit |
|----------|-----------|------------------------------------------|
| `IterativeLoopWorkflow.cs` | the loop itself | — |
| `VerifyAndRepairWorkflow.cs:65` | `while(true)` verify→repair | Completion condition is "verification gates pass" (`RunGatesAsync`), not a marker/classifier on the model's text. Each iteration runs 3 distinct activities (`RunGates` → `GenerateRepairPrompt` → `RunCliAgent`) driven by gate state. The repair loop's whole *point* is gate-driven control flow. |
| `SimpleAgentWorkflow.cs:120-158` | `for` coverage loop | Completion condition is structured `GradeCoverageAsync` output (`AllMet`). Each iteration runs 3 activities (`GradeCoverage` → `RunCliAgent` → `RunGates`). Replacing with `IterativeLoopWorkflow` would lose the structured coverage grader. |
| `FullOrchestrateWorkflow.cs:282` | `for` coverage loop (complex path) | Same as above — structured requirement grading per iteration. |
| `OrchestrateComplexPathWorkflow.cs:142` | `while (working.Count > 0)` | DAG task scheduler — iterates over ready tasks in a dependency graph. Not a "re-run the same prompt" pattern. |
| `WebsiteAuditLoopWorkflow.cs:58` | `foreach (sectionId in sections)` | Collection iteration — each loop body audits a *different* website section via its own child workflow. Not a "re-run until done" pattern. |

## Structural note

`IterativeLoopWorkflow` solves exactly one problem well: **run the same
prompt against a CLI agent, with a progress protocol, until the agent
self-reports done or the loop hits its ceiling.** Where a workflow's loop
has different semantics (structured grader, gate-driven repair, DAG
scheduling, N-collection fan-out), that specificity was preserved. The
one-off bespoke loops are documented here so future contributors can
decide case-by-case if they should fold in.

## Verification

- 319/319 tests green with `MinIterations=3, MaxIterations=20` on the
  research path, including the FullOrchestrate replay + integration tests
  (the stubbed `RunCliAgentAsync` now recognises the
  "MULTI-PASS deep research" prompt and returns a structured-progress
  response so the inner loop exits cleanly on each research pass).
- Live smoke with real Claude (workspace: `C:\AllGit\poc\games\rocketfifa`)
  against `ResearchPipeline` produced **5 iterations**, 12/12 tasks
  complete, 12 KB `research.md`, cost $0.98.
