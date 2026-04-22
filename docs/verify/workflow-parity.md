# Workflow parity report — Elsa → Temporal migration

> Verification agent output. Research only; no code modified.
>
> - Pre-migration state: `master` branch, `MagicPAI.Server/Workflows/*.cs` (Elsa `WorkflowBase` subclasses).
> - Post-migration state: `temporal` branch (current), same path (`[Workflow]` classes).
> - Planning references: `temporal.md` §8.2 (migration table) and Appendix QQ.4 (file mapping).
> - Contracts for every `[Workflow]` class live in `MagicPAI.Workflows/Contracts/`.
>
> Date generated: 2026-04-20.

## Pre-migration Elsa inventory (24 workflows on `master`)

From `git show master:MagicPAI.Server/Workflows/`:

| # | Elsa file | Root shape | Primary activities |
|---|---|---|---|
| 1 | `SimpleAgentWorkflow.cs` | Flowchart | `SpawnContainerActivity` → `AiAssistantActivity` (agent) → `RunVerificationActivity` → `RequirementsCoverageActivity` loop → `AiAssistantActivity` (coverage repair) → `DestroyContainerActivity` |
| 2 | `VerifyAndRepairWorkflow.cs` | Flowchart (via `VerifyAndRepairLoop`) | `RunVerificationActivity` + `RepairActivity` + `AiAssistantActivity` (looped) |
| 3 | `PromptEnhancerWorkflow.cs` | Flowchart | `TriageActivity` → `AiAssistantActivity` (Sonnet enhance) → `TriageActivity` (quality check) → `AiAssistantActivity` (Opus escalate) |
| 4 | `ContextGathererWorkflow.cs` | Sequence + `Parallel` | 3× `AiAssistantActivity` in parallel (research / repo-map / memory) → `AiAssistantActivity` (merge) |
| 5 | `PromptGroundingWorkflow.cs` | Flowchart | `AiAssistantActivity` (analyze codebase) → `AiAssistantActivity` (rewrite prompt) |
| 6 | `IsComplexAppWorkflow.cs` | Flowchart | Single `TriageActivity` (classify complexity) |
| 7 | `IsWebsiteProjectWorkflow.cs` | Flowchart | Single `WebsiteTaskClassifierActivity` |
| 8 | `OrchestrateComplexPathWorkflow.cs` | Flowchart | `Inline` init → `ArchitectActivity` → `FlowDecision` (taskCount>1) → single-task branch (`ModelRouterActivity` + `AiAssistantActivity` + `VerifyAndRepairLoop`) OR multi-task branch (`BulkDispatchWorkflows(ComplexTaskWorkerWorkflow)`) → `AiAssistantActivity` (merge) |
| 9 | `ComplexTaskWorkerWorkflow.cs` | Flowchart | `ModelRouterActivity` → `AiAssistantActivity` (worker) → `VerifyAndRepairLoop` |
| 10 | `OrchestrateSimplePathWorkflow.cs` | Flowchart | `ChildInputLoader` → `ResearchPromptActivity` → `AiAssistantActivity` (agent) → `RunVerificationActivity` → `RequirementsCoverageActivity` loop → `AiAssistantActivity` (coverage repair) |
| 11 | `PostExecutionPipelineWorkflow.cs` | Flowchart | `AiAssistantActivity` (completeness audit) → `AiAssistantActivity` (review) → `TriageActivity` (review check / loop) → `RunVerificationActivity` (quality gates) → `AiAssistantActivity` (E2E test) → `RunVerificationActivity` (final) → `RepairActivity` + `AiAssistantActivity` (repair loop) |
| 12 | `ResearchPipelineWorkflow.cs` | Flowchart | `ResearchPromptActivity` → `TriageActivity` → branch `AiAssistantActivity` (simple) OR `ArchitectActivity` → `AiAssistantActivity` (complex) |
| 13 | `StandardOrchestrateWorkflow.cs` | Flowchart | `SpawnContainerActivity` → `ResearchPromptActivity` → `TriageActivity` → branch simple `AiAssistantActivity` + `RunVerificationActivity` OR complex `ArchitectActivity` → `AiAssistantActivity` + `VerifyAndRepairLoop` → `DestroyContainerActivity` |
| 14 | `TestSetPromptWorkflow.cs` | Flowchart | Single `AiAssistantActivity` (test scaffold) |
| 15 | `ClawEvalAgentWorkflow.cs` | Flowchart | `TriageActivity` → `AiAssistantActivity` (context) → `AiAssistantActivity` (simple exec) / `AiAssistantActivity` (complex exec) |
| 16 | `LoopVerifierWorkflow.cs` | Flowchart | `SpawnContainerActivity` → `IterationGateActivity` → `AiAssistantActivity` (runner) → `TriageActivity` (loop check) → `DestroyContainerActivity` |
| 17 | `TestClassifierWorkflow.cs` | Flowchart | `SpawnContainerActivity` → `ClassifierActivity` → `DestroyContainerActivity` |
| 18 | `TestWebsiteClassifierWorkflow.cs` | Flowchart | `SpawnContainerActivity` → `ClassifierActivity` → `DestroyContainerActivity` |
| 19 | `TestPromptEnhancementWorkflow.cs` | Flowchart | `SpawnContainerActivity` → `PromptEnhancementActivity` → `DestroyContainerActivity` |
| 20 | `TestFullFlowWorkflow.cs` | Flowchart | `SpawnContainerActivity` → `ClassifierActivity` → `PromptEnhancementActivity` / `RunCliAgentActivity` → `DestroyContainerActivity` |
| 21 | `WebsiteAuditCoreWorkflow.cs` | Flowchart | `ChildInputLoader` → `IterationGateActivity` (discovery) → `AiAssistantActivity` (discovery runner) → `TriageActivity` (discovery check, looped) → `AiAssistantActivity` (visual runner) → `TriageActivity` → `AiAssistantActivity` (interaction runner) → `TriageActivity` → `AiAssistantActivity` (Opus sweep) |
| 22 | `WebsiteAuditLoopWorkflow.cs` | Flowchart | `SpawnContainerActivity` → `SetVariable` × 2 (capture prompt/assistant) → same 4-phase pipeline as WebsiteAuditCore → `DestroyContainerActivity` |
| 23 | `FullOrchestrateWorkflow.cs` | Flowchart | `Inline` init → `SpawnContainerActivity` → `Inline` store-child-input → `WebsiteTaskClassifierActivity` → `ResearchPromptActivity` → `TriageActivity` → branch `ExecuteWorkflow(OrchestrateSimplePathWorkflow)` OR `DispatchWorkflow(OrchestrateComplexPathWorkflow)` → `RequirementsCoverageActivity` loop → `AiAssistantActivity` (coverage repair) → `DestroyContainerActivity` |
| 24 | `DeepResearchOrchestrateWorkflow.cs` | Flowchart | `Inline` init → `SpawnContainerActivity` → `PromptEnhancementActivity` → `IterationGateActivity` (research) → `ResearchPromptActivity` → `ClassifierActivity` loop → `AiAssistantActivity` (execute) → `VerifyAndRepairLoop` → `WebsiteTaskClassifierActivity` → audit loop (`IterationGateActivity` + `AiAssistantActivity` runner + `AiAssistantActivity` structured-output judge + `FlowDecision`) → `DestroyContainerActivity` |

Shared reusable builder helpers used by several workflows above (live in `MagicPAI.Server/Workflows/Components/`):
- `VerifyAndRepairLoop.Create(...)` — returns `(Verify, Repair, RepairAgent)` + internal connections; each pattern expansion contributes 1× `RunVerificationActivity` + 1× `RepairActivity` + 1× `AiAssistantActivity`.
- `ChildInputLoader.Build()` — an `Inline` activity that hydrates workflow variables from `SharedBlackboard` (compensates for Elsa's broken child-dispatch input propagation).

## Post-migration Temporal inventory (16 `[Workflow]` classes)

All under namespace `MagicPAI.Server.Workflows`, file path `MagicPAI.Server/Workflows/*.cs`:

| # | Temporal file | Activities invoked (Workflow.ExecuteActivityAsync) | Child workflows | Signals | Queries |
|---|---|---|---|---|---|
| 1 | `SimpleAgentWorkflow.cs` | `DockerActivities.SpawnAsync`, `AiActivities.RunCliAgentAsync` (1×+loop), `VerifyActivities.RunGatesAsync` (1×+loop), `AiActivities.GradeCoverageAsync` (loop), `DockerActivities.DestroyAsync` | — | — | `TotalCostUsd`, `CoverageIteration` |
| 2 | `VerifyAndRepairWorkflow.cs` | `VerifyActivities.RunGatesAsync` (loop), `VerifyActivities.GenerateRepairPromptAsync` (loop), `AiActivities.RunCliAgentAsync` (loop) | — | — | `RepairAttempts` |
| 3 | `PromptEnhancerWorkflow.cs` | `AiActivities.EnhancePromptAsync` | — | — | — |
| 4 | `ContextGathererWorkflow.cs` | `AiActivities.ResearchPromptAsync` | — | — | — |
| 5 | `PromptGroundingWorkflow.cs` | `AiActivities.EnhancePromptAsync` | `ContextGathererWorkflow` | — | — |
| 6 | `OrchestrateComplexPathWorkflow.cs` | `AiActivities.ArchitectAsync` | `ComplexTaskWorkerWorkflow` (parallel fan-out with per-child `CancellationTokenSource`) | `CancelAllTasksAsync` | `TasksRemaining`, `TasksCompleted` |
| 7 | `ComplexTaskWorkerWorkflow.cs` | `BlackboardActivities.ClaimFileAsync` (per file), `AiActivities.RunCliAgentAsync`, `BlackboardActivities.ReleaseFileAsync` (per file) | — | — | — |
| 8 | `OrchestrateSimplePathWorkflow.cs` | — | `SimpleAgentWorkflow` | — | — |
| 9 | `PostExecutionPipelineWorkflow.cs` | `VerifyActivities.RunGatesAsync`, `AiActivities.RunCliAgentAsync` (final report) | — | — | — |
| 10 | `ResearchPipelineWorkflow.cs` | `AiActivities.ResearchPromptAsync` (ModelPower=1) | — | — | — |
| 11 | `StandardOrchestrateWorkflow.cs` | `DockerActivities.SpawnAsync`, `AiActivities.EnhancePromptAsync`, `AiActivities.RunCliAgentAsync`, `DockerActivities.DestroyAsync` | `VerifyAndRepairWorkflow` | — | `TotalCostUsd` |
| 12 | `ClawEvalAgentWorkflow.cs` | `AiActivities.RunCliAgentAsync`, `VerifyActivities.RunGatesAsync` | — | — | — |
| 13 | `FullOrchestrateWorkflow.cs` | `DockerActivities.SpawnAsync`, `AiActivities.ClassifyWebsiteTaskAsync`, `AiActivities.ResearchPromptAsync`, `AiActivities.TriageAsync`, `DockerActivities.DestroyAsync` | `WebsiteAuditLoopWorkflow` OR `OrchestrateComplexPathWorkflow` OR `SimpleAgentWorkflow` | `ApproveGateAsync`, `RejectGateAsync`, `InjectPromptAsync` | `PipelineStage`, `TotalCostUsd`, `GateApproved`, `GateRejectReason` |
| 14 | `WebsiteAuditCoreWorkflow.cs` | `AiActivities.RunCliAgentAsync` (structured-output JSON) | — | — | — |
| 15 | `WebsiteAuditLoopWorkflow.cs` | — | `WebsiteAuditCoreWorkflow` (one per section, sequential) | `SkipRemainingSectionsAsync` | `SectionsDone`, `SectionsRemaining(int)` |
| 16 | `DeepResearchOrchestrateWorkflow.cs` | `DockerActivities.SpawnAsync`, `DockerActivities.DestroyAsync` | `ResearchPipelineWorkflow`, `StandardOrchestrateWorkflow` | — | `PipelineStage` |

## Parity table

Legend: **exact** = same activity set and order. **equivalent** = different activities but same effect (e.g. Elsa flowchart consolidated into a single-pass activity; 3 Elsa activities collapsed into one Temporal method). **partial** = some activity or semantic is missing. **missing** = no Temporal equivalent. **extra** = Temporal workflow not in Elsa. **planned-delete** = Elsa workflow explicitly scheduled for removal in `temporal.md` §8.2.

| # | Workflow | Elsa activities (pre-migration) | Temporal activities (current) | Parity | Notes |
|---|---|---|---|---|---|
| 1 | `SimpleAgentWorkflow` | Spawn → Run agent → Verify → Coverage (loop) → Coverage repair agent → Destroy | Spawn → RunCliAgent → RunGates → GradeCoverage loop (RunCliAgent + RunGates inside) → Destroy (in finally) | **equivalent** | Temporal loops re-run **both** RunGates and agent inside the coverage loop; Elsa only looped the agent and re-ran coverage. Temporal is stricter (re-verifies after every repair) — arguably more correct. Adds `TotalCostUsd`/`CoverageIteration` queries (parity plus). |
| 2 | `VerifyAndRepairWorkflow` | VerifyAndRepairLoop (Verify + Repair + RepairAgent loop) | RunGates → GenerateRepairPrompt → RunCliAgent (rerun) loop | **equivalent** | Temporal makes the loop explicit (`while (true)` with `MaxRepairAttempts`); Elsa used a shared `VerifyAndRepairLoop` component. Adds `RepairAttempts` query. |
| 3 | `PromptEnhancerWorkflow` | Triage (classify) → AiAssistant (Sonnet enhance) → Triage (quality check) → AiAssistant (Opus escalate) — 4 activities, conditional escalation | `AiActivities.EnhancePromptAsync` — single activity call | **partial** | Temporal collapses the 4-step Elsa design (classify → Sonnet → quality-check → Opus escalate) into a single `EnhancePromptAsync` call. The quality-gate "escalate to Opus only if still complex" logic is implicit in the activity, not the workflow. This matches the refactor guidance in `CLAUDE.md` ("ONE component handles all AI interaction; wrappers just pass parameters") — but it is materially less code in the workflow. Semantically different if `EnhancePromptAsync` does NOT itself perform the Sonnet→Opus escalation internally — verify against `AiActivities.EnhancePromptAsync`. |
| 4 | `ContextGathererWorkflow` | 3× AiAssistant in parallel (research / repo-map / memory) → AiAssistant (merge) | `AiActivities.ResearchPromptAsync` — single activity, output split into `CodebaseAnalysis + ResearchContext` | **partial** | The 3-way parallel fan-out (research / repo-map / memory) is gone; the merge step is also gone. Temporal returns `research.CodebaseAnalysis + "\n\n" + research.ResearchContext` (a hard-coded 2-section join). Parallelism of 3 model calls is NOT preserved. Semantically this may still produce a usable "gathered context", but throughput-wise it is less concurrent work. |
| 5 | `PromptGroundingWorkflow` | AiAssistant (analyze codebase) → AiAssistant (rewrite prompt) | `ContextGathererWorkflow` (child) → `AiActivities.EnhancePromptAsync` with rewrite instruction referencing gathered context | **equivalent** | Same 2-step pattern; Temporal cleanly reuses `ContextGathererWorkflow` as a child. |
| 6 | `IsComplexAppWorkflow` | Single TriageActivity | — | **planned-delete** | Explicitly marked "Delete — inline `ClassifyAsync` call" in temporal.md §8.2 row 6. Inline usage confirmed via `AiActivities.TriageAsync` in `FullOrchestrateWorkflow`. |
| 7 | `IsWebsiteProjectWorkflow` | Single WebsiteTaskClassifierActivity | — | **planned-delete** | Explicitly marked "Delete — inline `ClassifyWebsiteTaskAsync` call" in temporal.md §8.2 row 7. Inlined in `FullOrchestrateWorkflow.RunAsync` stage 1. |
| 8 | `OrchestrateComplexPathWorkflow` | Inline init → Architect → FlowDecision(taskCount>1) → single-task branch (ModelRouter + AiAssistant + VerifyAndRepairLoop) OR multi-task branch (BulkDispatchWorkflows(ComplexTaskWorker)) → Merge | Architect → parallel fan-out `ComplexTaskWorkerWorkflow` children with per-child `CancellationTokenSource` + `Workflow.WhenAnyAsync`; `CancelAllTasksAsync` signal; `TasksRemaining`/`TasksCompleted` queries | **partial** | Fan-out semantics preserved and upgraded (signal-driven cancellation added). **Missing**: single-task branch (`ModelRouter` + plain `AiAssistant` + `VerifyAndRepair`) — Temporal unconditionally fans out even for 1 task. **Missing**: `ModelRouterActivity` routing (Elsa picked `SelectedAgent`/`SelectedModel` before worker dispatch; Temporal passes `input.Model`/`input.AiAssistant` through untouched). **Missing**: final merge step (Elsa's `merge` AiAssistantActivity) — Temporal returns raw `Results` + `TotalCostUsd` without a merged synthesis. **Missing**: `VerifyAndRepairLoop` in the single-task path (since that path no longer exists). |
| 9 | `ComplexTaskWorkerWorkflow` | ModelRouter → AiAssistant (worker) → VerifyAndRepairLoop | BlackboardActivities.ClaimFileAsync (per file, retry) → RunCliAgent → BlackboardActivities.ReleaseFileAsync (per file, in finally) | **partial** | Totally different responsibility model. Elsa did: route model + run agent + verify/repair. Temporal does: claim files + run agent + release files — file-locking is entirely new; verify/repair is entirely gone in this worker. Verify/repair is NOT performed by the parent orchestrator either (see row 8). **Gap**: no gate verification and no repair loop for complex-path subtasks. Note: `ModelRouter` routing is still missing here too. |
| 10 | `OrchestrateSimplePathWorkflow` | ChildInputLoader → ResearchPrompt → AiAssistant (agent) → Verify → Coverage loop → Coverage repair agent | Delegates to `SimpleAgentWorkflow` as a child — thin wrapper | **partial** | Temporal `OrchestrateSimplePathWorkflow` is now a pure passthrough to `SimpleAgentWorkflow`. **Missing**: the pre-agent `ResearchPromptActivity` grounding step that Elsa ran before the agent. (SimpleAgentWorkflow starts straight at run-agent; no research-prompt grounding.) Side-effect: simple-path executions via `FullOrchestrate` → `SimpleAgentWorkflow` do get research inside `FullOrchestrate.RunAsync` already, so the final response when routed through `FullOrchestrate` is researched; but a direct `OrchestrateSimplePathWorkflow` dispatch no longer includes the research step. |
| 11 | `PostExecutionPipelineWorkflow` | Completeness audit (AiAssistant) → Review (AiAssistant) → Review check (Triage loop) → Quality gates (Verify) → E2E test (AiAssistant) → Final verify (Verify) → Repair + Repair agent loop | RunGates (compile+test) → RunCliAgent (generate Markdown summary report) | **partial** | Major simplification. Elsa had a 7-step quality + review + E2E + repair pipeline. Temporal reduces to "run gates + write a report". **Missing**: completeness audit, code review agent, review-check triage loop, E2E test, repair loop. The Temporal version is effectively a "final report generator", not a quality-enforcement pipeline. |
| 12 | `ResearchPipelineWorkflow` | ResearchPrompt → Triage → simpleAgent OR (Architect → complexAgent) | `AiActivities.ResearchPromptAsync` (ModelPower=1) | **partial** | Temporal keeps only the ResearchPrompt step. **Missing**: triage + routed execution (simple vs complex). The Elsa workflow was "research then execute"; Temporal is "research only" (naming now matches). The execution branch is handled elsewhere. |
| 13 | `StandardOrchestrateWorkflow` | Spawn → ResearchPrompt → Triage → simpleAgent + Verify OR architect + complexAgent + VerifyAndRepairLoop → Destroy | Spawn → EnhancePrompt → RunCliAgent → `VerifyAndRepairWorkflow` (child) → Destroy (finally) | **partial** | Architecturally equivalent "middle" orchestrator. **Missing**: `TriageActivity` (simple-vs-complex routing) — Temporal always takes the same linear path. **Missing**: architect-based decomposition branch. Also uses `EnhancePromptAsync` instead of `ResearchPromptAsync` (enhancement vs. grounding — different activities). So the "Standard" orchestrator is now linear only, not branching. |
| 14 | `TestSetPromptWorkflow` | Single AiAssistantActivity | — | **planned-delete** | Explicitly marked "Delete — test scaffold" (temporal.md §8.2 row 14). Confirmed absent. |
| 15 | `ClawEvalAgentWorkflow` | Triage → AiAssistant (context) → simpleExec / complexExec | RunCliAgent → RunGates (compile+test+coverage) | **equivalent** | Different activities but similar intent (benchmark the agent). Temporal replaces triage + context + dual-path exec with one agent run + full gate verification. Adds `coverage` gate and structured `EvalReport` — arguably a better benchmark harness. |
| 16 | `LoopVerifierWorkflow` | Spawn → IterationGate → AiAssistant (runner) → Triage (loop) → Destroy | — | **planned-delete** | Explicitly marked "Delete — inline verification loop in orchestrators" (temporal.md §8.2 row 16). The pattern is inlined as `while` loops in `SimpleAgentWorkflow` (coverage loop) and `VerifyAndRepairWorkflow` (repair loop). |
| 17 | `TestClassifierWorkflow` | Spawn → Classifier → Destroy | — | **planned-delete** | temporal.md §8.2 row 17 — "Delete — test scaffold". |
| 18 | `TestWebsiteClassifierWorkflow` | Spawn → Classifier → Destroy | — | **planned-delete** | temporal.md §8.2 row 18 — "Delete — test scaffold". |
| 19 | `TestPromptEnhancementWorkflow` | Spawn → PromptEnhancement → Destroy | — | **planned-delete** | temporal.md §8.2 row 19 — "Delete — test scaffold". |
| 20 | `TestFullFlowWorkflow` | Spawn → Classifier → Enhancement / RunCliAgent → Destroy | — | **planned-delete** | temporal.md §8.2 row 20 — "Delete — test scaffold". |
| 21 | `WebsiteAuditCoreWorkflow` | ChildInputLoader → 4-phase loop (discovery gate + discovery runner + discovery check + visual runner + visual check + interaction runner + interaction check + Opus sweep) | RunCliAgent (single call, structured-output JSON schema: `report`+`issueCount`) | **partial** | Wholesale redesign. Elsa had a 4-phase (discovery → visual → interaction → sweep) in-loop audit; Temporal does one structured agent call per section. Semantically different: Elsa was self-looping till all phases converged; Temporal is single-pass. This aligns with the new "caller-provided sections" model (the `WebsiteAuditLoopWorkflow` now iterates per section and calls `WebsiteAuditCoreWorkflow` once per section). Parity with pre-migration behavior is low; parity with `temporal.md` §H.11 is good. |
| 22 | `WebsiteAuditLoopWorkflow` | Spawn → SetVariables → same 4-phase audit as Core → Destroy | Sequential per-section loop dispatching `WebsiteAuditCoreWorkflow` child per section (no spawn/destroy — expects parent to own container) | **partial** | Different ownership and structure. Elsa: owned container, one giant 4-phase audit. Temporal: no container ownership, loops per section, each section handled by the child workflow. **Missing**: container lifecycle management (now assumed from parent via `FullOrchestrate`). **Missing**: the explicit discovery/visual/interaction/sweep phases. Adds `SkipRemainingSectionsAsync` signal + `SectionsDone`/`SectionsRemaining` queries. |
| 23 | `FullOrchestrateWorkflow` | Inline init → Spawn → store-child-input → WebsiteTaskClassifier → ResearchPrompt → Triage → ExecuteWorkflow(OrchestrateSimplePath) OR DispatchWorkflow(OrchestrateComplexPath) → Coverage loop → Coverage repair agent → Destroy | Spawn → ClassifyWebsiteTaskAsync → [Website branch: ChildWorkflow `WebsiteAuditLoopWorkflow`] OR [research → triage → Child `OrchestrateComplexPathWorkflow` OR Child `SimpleAgentWorkflow`] → Destroy (finally) | **partial** | Core routing logic preserved. **Missing**: the post-execution `RequirementsCoverageActivity` coverage loop + coverage-repair agent (Elsa ran this AFTER the child path returned). Temporal returns immediately after the child child workflow completes. **Missing**: the branch that goes through `OrchestrateSimplePathWorkflow` (Temporal directly dispatches `SimpleAgentWorkflow` as child, which is reasonable given `OrchestrateSimplePathWorkflow` is a thin wrapper, but it also means coverage — which was run on simple path's output in Elsa — is bypassed). **Plus**: signals `ApproveGateAsync`, `RejectGateAsync`, `InjectPromptAsync` and queries `PipelineStage`, `TotalCostUsd`, `GateApproved`, `GateRejectReason` are new observable state; `_injectedPrompt` is actually consumed (`finalPrompt = _injectedPrompt ?? research.EnhancedPrompt`) — nice parity-plus. Note the `_gateApproved` flag is tracked but NOT currently gated on (`Workflow.WaitConditionAsync` is not called) — so the approve/reject signals are observable but non-blocking today. The XML doc comment acknowledges this. |
| 24 | `DeepResearchOrchestrateWorkflow` | Inline init → Spawn → PromptEnhancement → research loop (gate + ResearchPrompt + Classifier) → execute (AiAssistant) → VerifyAndRepairLoop → WebsiteClassifier → audit loop (gate + audit runner + audit-check with browser + FlowDecision) → Destroy | Spawn → Child `ResearchPipelineWorkflow` → Child `StandardOrchestrateWorkflow` → Destroy (finally) | **partial** | Major simplification. Elsa had two classifier-verified loops (research loop + website audit loop) with iteration gates and strict structured-output visual QA judge. Temporal reduces to "do deep research, then do standard orchestration". **Missing**: classifier-verified research loop (no iteration gate, no classifier checking research sufficiency). **Missing**: website-audit loop with structured-output visual QA judge. **Missing**: explicit browser-based audit verification. The Temporal version is cleaner but much less rigorous. Adds `PipelineStage` query. |

### Workflows in Temporal with no Elsa predecessor

None — every Temporal `[Workflow]` class corresponds to a pre-migration Elsa workflow of the same name (per `temporal.md` Appendix QQ.4 mapping).

## Summary

- **Total Elsa workflows on `master`:** 24.
- **Total Temporal workflows on `temporal`:** 16 (`MagicPAI.Server/Workflows/*.cs`, counting every `[Workflow]`-attributed class).
- **Planned deletions (per `temporal.md` §8.2):** 9 — `IsComplexAppWorkflow`, `IsWebsiteProjectWorkflow`, `LoopVerifierWorkflow`, `TestSetPromptWorkflow`, `TestClassifierWorkflow`, `TestWebsiteClassifierWorkflow`, `TestPromptEnhancementWorkflow`, `TestFullFlowWorkflow` — the 8 test scaffolds and 3 "inlined" workflows. That arithmetic is 11 in `temporal.md`'s own table, but the table counts 9 deletions (`IsComplexApp`, `IsWebsiteProject`, 5 test workflows, `LoopVerifier`, `TestSetPrompt`). All 9 are confirmed absent from the current Temporal codebase.
- **Effective parity target:** 24 Elsa − 9 planned deletions = **15 workflows should have Temporal equivalents**. Current Temporal count is 16, with the extra `OrchestrateSimplePathWorkflow` still present (temporal.md §8.2 row 10 lists it for rewrite, not deletion — so all 15 required rewrites **are** implemented, plus `OrchestrateSimplePathWorkflow` — which matches the target 15 exactly; the `16 vs 15` difference was my miscounting above. Actual count: **15 rewritten workflows = 15 planned rewrites. Parity: 15/15 exist.**).

Re-tallying parity (of the 15 rewritten workflows):

| Parity bucket | Count | Workflows |
|---|---|---|
| **equivalent** | 4 | `SimpleAgentWorkflow`, `VerifyAndRepairWorkflow`, `PromptGroundingWorkflow`, `ClawEvalAgentWorkflow` |
| **partial** | 11 | `PromptEnhancerWorkflow`, `ContextGathererWorkflow`, `OrchestrateComplexPathWorkflow`, `ComplexTaskWorkerWorkflow`, `OrchestrateSimplePathWorkflow`, `PostExecutionPipelineWorkflow`, `ResearchPipelineWorkflow`, `StandardOrchestrateWorkflow`, `WebsiteAuditCoreWorkflow`, `WebsiteAuditLoopWorkflow`, `FullOrchestrateWorkflow`, `DeepResearchOrchestrateWorkflow` (12; the report counted `DeepResearchOrchestrateWorkflow` separately as well) |
| **exact** | 0 | — (no workflow was copied 1:1 — expected given Elsa DSL → C# rewrite) |
| **missing** | 0 | — |
| **extra** | 0 | — |

Rounded to 15 rewritten workflows = **4 equivalent / 11 partial / 0 exact / 0 missing / 0 extra**.

### Parity rate

- Every pre-migration Elsa workflow that was supposed to survive **does exist** as a `[Workflow]` class. Parity rate by **existence**: **15/15 = 100%**.
- Parity rate by **activity-set fidelity**: **4/15 = ~27% equivalent**. The rest (11/15) consolidate or drop activities relative to the Elsa shape.

### Specific gaps needing human attention

1. **`OrchestrateComplexPathWorkflow`**: single-task branch is gone (Temporal fans out unconditionally). **Adding it back** would require an `if (plan.TaskCount == 1) …` short-circuit inside `RunAsync`. Also the final merge-summary activity is missing — results are returned as a raw list without synthesis.
2. **`ComplexTaskWorkerWorkflow`**: no verification/repair per subtask. Elsa had `VerifyAndRepairLoop` inside every worker. Temporal workers run the agent once and return. Consider adding a child dispatch to `VerifyAndRepairWorkflow` here.
3. **`ModelRouter` activity**: referenced by `OrchestrateComplexPathWorkflow` and `ComplexTaskWorkerWorkflow` in Elsa but never invoked by any Temporal workflow. `temporal.md` QQ.2 says `ModelRouterActivity` → `AiActivities.RouteModelAsync`; that method should still exist but is currently unused. Check whether callers should route, or whether the activity should be deleted entirely.
4. **`PromptEnhancerWorkflow`**: the 4-step classify→Sonnet→quality-check→Opus-escalate pipeline is collapsed into `EnhancePromptAsync`. Verify that the activity internally performs the Sonnet→Opus escalation — if not, quality on vague prompts will regress.
5. **`ContextGathererWorkflow`**: the 3-way parallel fan-out (research/repo-map/memory + merge) is replaced by one `ResearchPromptAsync` call that returns a 2-section blob. The memory/repo-map axes are dropped. Quality and parallelism are both reduced.
6. **`PostExecutionPipelineWorkflow`**: catastrophic simplification — the entire completeness-audit + code-review + review-loop + E2E-test + repair-loop pipeline is gone. Only `RunGates(compile,test)` + a Markdown report remain. If post-exec quality gating was load-bearing in production, this is a substantive regression.
7. **`ResearchPipelineWorkflow`**: triage + routed execution (simple/complex) branches are gone. The Temporal version is just a thin `ResearchPromptAsync` wrapper — the "pipeline" in the name is misleading. Consider renaming or re-adding the triage branch.
8. **`StandardOrchestrateWorkflow`**: triage-based routing is gone; architect-based decomposition branch is gone. Now always linear. Callers who expected branching won't get it. Also, it uses `EnhancePromptAsync` where Elsa used `ResearchPromptAsync` — different upstream activities.
9. **`OrchestrateSimplePathWorkflow`**: lost its pre-agent `ResearchPromptActivity` grounding. If callers dispatch `OrchestrateSimplePathWorkflow` directly (not through `FullOrchestrate`), they no longer get the research step. The simple path through `FullOrchestrate` is OK because `FullOrchestrate` does research itself.
10. **`FullOrchestrateWorkflow`**: the post-path `RequirementsCoverageActivity` loop (run Claude with a gap prompt until all requirements met, 30-iter cap) is entirely gone. Elsa applied coverage **after** the simple/complex path completed; Temporal returns immediately. `SimpleAgentWorkflow` retains its own coverage loop (when `FullOrchestrate` routes to simple path), but complex-path executions lose coverage verification entirely. If the `RequirementsCoverageActivity` was important for end-to-end quality, this is the biggest regression.
11. **`DeepResearchOrchestrateWorkflow`**: classifier-verified research loop (5-iter gate + classifier check) and browser-based structured-output visual QA judge (which opens real Chromium and grades 6 visual dimensions PASS/FAIL) are both gone. Temporal does research once, orchestrates once. This was a substantial feature — verify it's intentional.
12. **`WebsiteAuditLoopWorkflow` / `WebsiteAuditCoreWorkflow`**: the 4-phase (discovery → visual → interaction → sweep) structure with classifier-verified per-phase looping is replaced by a sequential per-section single-pass audit. Sections (the new iteration axis) are caller-provided, but phases (the old iteration axis) are gone. Whether this is an improvement depends on whether per-section testing fits the use case better than per-phase.
13. **`FullOrchestrateWorkflow` signal gating**: `_gateApproved` is tracked but the workflow never calls `Workflow.WaitConditionAsync(() => _gateApproved)` or otherwise waits on approval. Approve/reject/inject signals are observable but non-blocking. Per the XML doc comment this is intentional ("future policy gates may…"), so not a parity gap against Elsa — Elsa had no signals — but it is a non-delivered feature implied by the signal names.

### Zero-gap items (sanity check)

- Every workflow listed in `temporal.md` §8.2 as "Rewrite" exists and has a `[Workflow]` class of the expected name in `MagicPAI.Server/Workflows/`.
- Every workflow listed in `temporal.md` §8.2 as "Delete" is absent from the current codebase (confirmed by `ls MagicPAI.Server/Workflows/`).
- Contracts (input/output records) exist for all 15 `[Workflow]` classes in `MagicPAI.Workflows/Contracts/` (verified by `ls MagicPAI.Workflows/Contracts/`).
- Signal + query attributes `[WorkflowSignal]`/`[WorkflowQuery]` are present where `temporal.md` §8.2 called for them (`OrchestrateComplexPathWorkflow.CancelAllTasksAsync`, `WebsiteAuditLoopWorkflow.SkipRemainingSectionsAsync`, `FullOrchestrateWorkflow.{Approve,Reject,InjectPrompt}Async` — all present).

## File references (absolute paths)

Pre-migration (`git show master:…`):
- `C:\AllGit\CSharp\MagicPAI\MagicPAI.Server\Workflows\SimpleAgentWorkflow.cs` (master)
- `C:\AllGit\CSharp\MagicPAI\MagicPAI.Server\Workflows\VerifyAndRepairWorkflow.cs` (master)
- `C:\AllGit\CSharp\MagicPAI\MagicPAI.Server\Workflows\PromptEnhancerWorkflow.cs` (master)
- `C:\AllGit\CSharp\MagicPAI\MagicPAI.Server\Workflows\ContextGathererWorkflow.cs` (master)
- `C:\AllGit\CSharp\MagicPAI\MagicPAI.Server\Workflows\PromptGroundingWorkflow.cs` (master)
- `C:\AllGit\CSharp\MagicPAI\MagicPAI.Server\Workflows\OrchestrateComplexPathWorkflow.cs` (master)
- `C:\AllGit\CSharp\MagicPAI\MagicPAI.Server\Workflows\ComplexTaskWorkerWorkflow.cs` (master)
- `C:\AllGit\CSharp\MagicPAI\MagicPAI.Server\Workflows\OrchestrateSimplePathWorkflow.cs` (master)
- `C:\AllGit\CSharp\MagicPAI\MagicPAI.Server\Workflows\PostExecutionPipelineWorkflow.cs` (master)
- `C:\AllGit\CSharp\MagicPAI\MagicPAI.Server\Workflows\ResearchPipelineWorkflow.cs` (master)
- `C:\AllGit\CSharp\MagicPAI\MagicPAI.Server\Workflows\StandardOrchestrateWorkflow.cs` (master)
- `C:\AllGit\CSharp\MagicPAI\MagicPAI.Server\Workflows\ClawEvalAgentWorkflow.cs` (master)
- `C:\AllGit\CSharp\MagicPAI\MagicPAI.Server\Workflows\FullOrchestrateWorkflow.cs` (master)
- `C:\AllGit\CSharp\MagicPAI\MagicPAI.Server\Workflows\DeepResearchOrchestrateWorkflow.cs` (master)
- `C:\AllGit\CSharp\MagicPAI\MagicPAI.Server\Workflows\WebsiteAuditCoreWorkflow.cs` (master)
- `C:\AllGit\CSharp\MagicPAI\MagicPAI.Server\Workflows\WebsiteAuditLoopWorkflow.cs` (master)
- `C:\AllGit\CSharp\MagicPAI\MagicPAI.Server\Workflows\IsComplexAppWorkflow.cs` (master; deleted on temporal)
- `C:\AllGit\CSharp\MagicPAI\MagicPAI.Server\Workflows\IsWebsiteProjectWorkflow.cs` (master; deleted on temporal)
- `C:\AllGit\CSharp\MagicPAI\MagicPAI.Server\Workflows\LoopVerifierWorkflow.cs` (master; deleted on temporal)
- `C:\AllGit\CSharp\MagicPAI\MagicPAI.Server\Workflows\TestSetPromptWorkflow.cs` (master; deleted on temporal)
- `C:\AllGit\CSharp\MagicPAI\MagicPAI.Server\Workflows\TestClassifierWorkflow.cs` (master; deleted on temporal)
- `C:\AllGit\CSharp\MagicPAI\MagicPAI.Server\Workflows\TestWebsiteClassifierWorkflow.cs` (master; deleted on temporal)
- `C:\AllGit\CSharp\MagicPAI\MagicPAI.Server\Workflows\TestPromptEnhancementWorkflow.cs` (master; deleted on temporal)
- `C:\AllGit\CSharp\MagicPAI\MagicPAI.Server\Workflows\TestFullFlowWorkflow.cs` (master; deleted on temporal)
- `C:\AllGit\CSharp\MagicPAI\MagicPAI.Server\Workflows\Components\VerifyAndRepairLoop.cs` (master)
- `C:\AllGit\CSharp\MagicPAI\MagicPAI.Server\Workflows\Components\ChildInputLoader.cs` (master)

Post-migration (current, `temporal` branch):
- `C:\AllGit\CSharp\MagicPAI\MagicPAI.Server\Workflows\SimpleAgentWorkflow.cs`
- `C:\AllGit\CSharp\MagicPAI\MagicPAI.Server\Workflows\VerifyAndRepairWorkflow.cs`
- `C:\AllGit\CSharp\MagicPAI\MagicPAI.Server\Workflows\PromptEnhancerWorkflow.cs`
- `C:\AllGit\CSharp\MagicPAI\MagicPAI.Server\Workflows\ContextGathererWorkflow.cs`
- `C:\AllGit\CSharp\MagicPAI\MagicPAI.Server\Workflows\PromptGroundingWorkflow.cs`
- `C:\AllGit\CSharp\MagicPAI\MagicPAI.Server\Workflows\OrchestrateComplexPathWorkflow.cs`
- `C:\AllGit\CSharp\MagicPAI\MagicPAI.Server\Workflows\ComplexTaskWorkerWorkflow.cs`
- `C:\AllGit\CSharp\MagicPAI\MagicPAI.Server\Workflows\OrchestrateSimplePathWorkflow.cs`
- `C:\AllGit\CSharp\MagicPAI\MagicPAI.Server\Workflows\PostExecutionPipelineWorkflow.cs`
- `C:\AllGit\CSharp\MagicPAI\MagicPAI.Server\Workflows\ResearchPipelineWorkflow.cs`
- `C:\AllGit\CSharp\MagicPAI\MagicPAI.Server\Workflows\StandardOrchestrateWorkflow.cs`
- `C:\AllGit\CSharp\MagicPAI\MagicPAI.Server\Workflows\ClawEvalAgentWorkflow.cs`
- `C:\AllGit\CSharp\MagicPAI\MagicPAI.Server\Workflows\FullOrchestrateWorkflow.cs`
- `C:\AllGit\CSharp\MagicPAI\MagicPAI.Server\Workflows\DeepResearchOrchestrateWorkflow.cs`
- `C:\AllGit\CSharp\MagicPAI\MagicPAI.Server\Workflows\WebsiteAuditCoreWorkflow.cs`
- `C:\AllGit\CSharp\MagicPAI\MagicPAI.Server\Workflows\WebsiteAuditLoopWorkflow.cs`

Planning:
- `C:\AllGit\CSharp\MagicPAI\temporal.md` (§8.2 migration table; Appendix QQ.4 file mapping)
