// MagicPAI.Server/Workflows/Temporal/FullOrchestrateWorkflow.cs
// Temporal port of the Elsa FullOrchestrateWorkflow — the central orchestrator.
// Routes a session through one of three pipelines (website audit / simple /
// complex) based on website-classification and triage verdicts. Exposes three
// signals (gate approve/reject, prompt injection) and two queries (pipeline
// stage, total cost). See temporal.md §8.6.
using Temporalio.Workflows;
using MagicPAI.Activities.AI;
using MagicPAI.Activities.Contracts;
using MagicPAI.Activities.Docker;
using MagicPAI.Activities.Stage;
using MagicPAI.Workflows;
using MagicPAI.Workflows.Contracts;

namespace MagicPAI.Server.Workflows;

/// <summary>
/// Central orchestrator. Spawns a container, classifies whether the task is
/// website-related, and routes accordingly: website-audit child workflow,
/// complex-path child, or simple-path child. Container lifetime is owned here
/// — destroy happens in <c>finally</c>. See temporal.md §8.6 for the reference.
/// </summary>
/// <remarks>
/// <para><b>Signals.</b> Three signal handlers, each async Task (required by the
/// Temporal SDK even when the body is synchronous) and side-effect only:
/// <see cref="ApproveGateAsync"/>, <see cref="RejectGateAsync"/>,
/// <see cref="InjectPromptAsync"/>. The run body consumes the injected prompt
/// by substituting it for the research-enhanced prompt after triage completes.
/// </para>
/// <para><b>Queries.</b> <see cref="PipelineStage"/> reports the human-readable
/// pipeline phase; <see cref="TotalCostUsd"/> accumulates across branches.
/// Both are observable while the workflow runs.
/// </para>
/// </remarks>
[Workflow]
public class FullOrchestrateWorkflow
{
    // Observable state.
    private string _pipelineStage = "initializing";
    private decimal _totalCost;
    private string? _injectedPrompt;
    private bool _gateApproved;
    private string? _gateRejectReason;
    private int _coverageIteration;

    [WorkflowQuery]
    public string PipelineStage => _pipelineStage;

    [WorkflowQuery]
    public decimal TotalCostUsd => _totalCost;

    /// <summary>
    /// Coverage-loop counter exposed for Studio progress. Counts iterations
    /// entered in the complex-path post-execution coverage check. Zero on the
    /// simple/website-audit branches (their own workflows track coverage).
    /// </summary>
    [WorkflowQuery]
    public int CoverageIteration => _coverageIteration;

    /// <summary>
    /// Observable gate-approval state. Surfaced as a query so Studio can
    /// render an approval-pending badge while a signal is awaited by upstream
    /// policy. The §8.6 reference stores it but does not gate on it — future
    /// policy gates may <c>Workflow.WaitConditionAsync(() =&gt; _gateApproved)</c>.
    /// </summary>
    [WorkflowQuery]
    public bool GateApproved => _gateApproved;

    [WorkflowQuery]
    public string? GateRejectReason => _gateRejectReason;

    /// <summary>
    /// Observable HITL state. <c>true</c> while the workflow is parked at the
    /// triage-approval gate awaiting an <see cref="ApproveGateAsync"/> or
    /// <see cref="RejectGateAsync"/> signal. Studio renders an approval prompt
    /// when this flips true.
    /// </summary>
    [WorkflowQuery]
    public bool AwaitingApproval => _pipelineStage == "awaiting-gate-approval";

    [WorkflowSignal]
    public Task ApproveGateAsync(string approver)
    {
        _gateApproved = true;
        return Task.CompletedTask;
    }

    [WorkflowSignal]
    public Task RejectGateAsync(string reason)
    {
        _gateRejectReason = reason;
        return Task.CompletedTask;
    }

    [WorkflowSignal]
    public Task InjectPromptAsync(string newPrompt)
    {
        _injectedPrompt = newPrompt;
        return Task.CompletedTask;
    }

    [WorkflowRun]
    public async Task<FullOrchestrateOutput> RunAsync(FullOrchestrateInput input)
    {
        // Container-lifecycle branching. SmartImproveWorkflow (and any future
        // orchestrator that wants to share container state across multiple
        // FullOrchestrate runs) hands its container in via ExistingContainerId.
        // When null, the original spawn-and-destroy lifecycle applies.
        //
        // Wrapped in Workflow.Patched so old workflow histories — which always
        // emitted a SpawnAsync activity at this point — replay deterministically
        // even on workers that have the new branch compiled in. See PATCHES.md
        // and CLAUDE.md §"Workflow versioning".
        string containerId;
        bool ownsContainer;
        var canHandoff = Workflow.Patched("full-orchestrate-container-handoff-v1");
        if (canHandoff && !string.IsNullOrWhiteSpace(input.ExistingContainerId))
        {
            _pipelineStage = "container-handoff";
            await EmitStageAsync(input.SessionId, "container-handoff");
            containerId = input.ExistingContainerId;
            ownsContainer = false;
        }
        else
        {
            _pipelineStage = "spawning-container";
            await EmitStageAsync(input.SessionId, "spawning-container");

            var spawnInput = new SpawnContainerInput(
                SessionId: input.SessionId,
                WorkspacePath: input.WorkspacePath,
                EnableGui: input.EnableGui);

            var spawn = await Workflow.ExecuteActivityAsync(
                (DockerActivities a) => a.SpawnAsync(spawnInput),
                ActivityProfiles.Container);

            containerId = spawn.ContainerId;
            ownsContainer = true;
        }

        try
        {
            // Stage 1 — website classification gate.
            _pipelineStage = "classifying-website";
            await EmitStageAsync(input.SessionId, "classifying-website");

            var classifyInput = new WebsiteClassifyInput(
                Prompt: input.Prompt,
                ContainerId: containerId,
                AiAssistant: input.AiAssistant,
                SessionId: input.SessionId);

            var websiteClass = await Workflow.ExecuteActivityAsync(
                (AiActivities a) => a.ClassifyWebsiteTaskAsync(classifyInput),
                ActivityProfiles.Medium);

            if (websiteClass.IsWebsiteTask)
            {
                _pipelineStage = "website-audit";
                await EmitStageAsync(input.SessionId, "website-audit");

                var siteInput = new WebsiteAuditInput(
                    SessionId: input.SessionId,
                    ContainerId: containerId,
                    Prompt: input.Prompt,
                    WorkspacePath: input.WorkspacePath,
                    AiAssistant: input.AiAssistant,
                    Model: input.Model);

                var siteResult = await Workflow.ExecuteChildWorkflowAsync(
                    (WebsiteAuditLoopWorkflow w) => w.RunAsync(siteInput),
                    new ChildWorkflowOptions { Id = $"{input.SessionId}-website" });

                _totalCost += siteResult.CostUsd;
                await EmitCostAsync(input.SessionId, _totalCost);
                _pipelineStage = "completed";
                await EmitStageAsync(input.SessionId, "completed");

                return new FullOrchestrateOutput(
                    PipelineUsed: "website-audit",
                    FinalResponse: siteResult.Summary,
                    TotalCostUsd: _totalCost);
            }

            // Stage 2 — research the prompt for grounding.
            // Dispatches the iterative ResearchPipeline child workflow so
            // Claude runs the full multi-pass research protocol (A: framing,
            // B: options & trade-offs, C: plan/risks/verification) instead of
            // a single shallow ResearchPromptAsync call. The research loop
            // reuses this workflow's container (MinIterations=3, MaxIter=20)
            // and writes research.md into the workspace as a side-effect.
            _pipelineStage = "research-prompt";
            await EmitStageAsync(input.SessionId, "research-prompt");

            var researchInput = new ResearchPipelineInput(
                SessionId: input.SessionId,
                Prompt: input.Prompt,
                ContainerId: containerId,
                WorkingDirectory: input.WorkspacePath,
                AiAssistant: input.AiAssistant);

            var research = await Workflow.ExecuteChildWorkflowAsync(
                (ResearchPipelineWorkflow w) => w.RunAsync(researchInput),
                new ChildWorkflowOptions { Id = $"{input.SessionId}-research" });

            _totalCost += research.CostUsd;
            await EmitCostAsync(input.SessionId, _totalCost);

            // If the research loop produced no useable rewrite (e.g. max
            // iterations fired early), fall back to the original user prompt
            // so downstream triage has something concrete to chew on.
            var groundedPrompt = string.IsNullOrWhiteSpace(research.ResearchedPrompt)
                ? input.Prompt
                : research.ResearchedPrompt;

            // Stage 3 — triage to decide complexity.
            _pipelineStage = "triage";
            await EmitStageAsync(input.SessionId, "triage");

            var triageInput = new TriageInput(
                Prompt: groundedPrompt,
                ContainerId: containerId,
                ClassificationInstructions: null,
                AiAssistant: input.AiAssistant,
                SessionId: input.SessionId,
                ComplexityThreshold: input.ComplexityThreshold);

            var triage = await Workflow.ExecuteActivityAsync(
                (AiActivities a) => a.TriageAsync(triageInput),
                ActivityProfiles.Medium);

            // Optional HITL gate — parks the workflow until ApproveGate or
            // RejectGate is signalled. Disabled by default; flip the input
            // field on when an operator wants to vet the triage verdict before
            // a (potentially expensive) complex-path run.
            if (input.RequireTriageApproval)
            {
                _pipelineStage = "awaiting-gate-approval";
                await EmitStageAsync(input.SessionId, "awaiting-gate-approval");

                var timeoutHours = input.GateApprovalTimeoutHours > 0
                    ? input.GateApprovalTimeoutHours
                    : 24;

                // Workflow.WaitConditionAsync(predicate, timeout) returns false
                // on timeout and true when the predicate evaluated truthy. It
                // does not throw TimeoutException. See temporal.md §ZZ.7.
                var decided = await Workflow.WaitConditionAsync(
                    () => _gateApproved || _gateRejectReason != null,
                    TimeSpan.FromHours(timeoutHours));

                if (!decided)
                {
                    // Park-time exhausted — treat the same as an explicit reject
                    // so the workflow takes a single, well-defined exit path.
                    _gateRejectReason = "timeout";
                }

                if (_gateRejectReason != null)
                {
                    _pipelineStage = "rejected";
                    await EmitStageAsync(input.SessionId, "rejected");
                    return new FullOrchestrateOutput(
                        PipelineUsed: "rejected",
                        FinalResponse: $"Gate rejected: {_gateRejectReason}",
                        TotalCostUsd: _totalCost);
                }
            }

            // An InjectPrompt signal received at any point before now overrides
            // the research-grounded prompt for the downstream branch.
            var finalPrompt = _injectedPrompt ?? groundedPrompt;

            FullOrchestrateOutput result;
            if (triage.IsComplex)
            {
                _pipelineStage = "complex-path";
                await EmitStageAsync(input.SessionId, "complex-path");

                var complexInput = new OrchestrateComplexInput(
                    SessionId: input.SessionId,
                    Prompt: finalPrompt,
                    ContainerId: containerId,
                    WorkspacePath: input.WorkspacePath,
                    AiAssistant: input.AiAssistant,
                    Model: triage.RecommendedModel,
                    ModelPower: triage.RecommendedModelPower);

                var complex = await Workflow.ExecuteChildWorkflowAsync(
                    (OrchestrateComplexPathWorkflow w) => w.RunAsync(complexInput),
                    new ChildWorkflowOptions { Id = $"{input.SessionId}-complex" });

                _totalCost += complex.TotalCostUsd;
                await EmitCostAsync(input.SessionId, _totalCost);

                // Post-execution requirements-coverage loop. Unlike SimpleAgent's
                // coverage loop (which runs inside its own child workflow), the
                // complex path delegates subtask execution to OrchestrateComplexPath
                // and then asks the grader whether the top-level prompt's
                // requirements were actually met by the decomposed + reassembled
                // output. On gaps we run ONE direct agent pass with the gap prompt
                // per iteration — NOT a fresh OrchestrateComplexPath child,
                // because re-architecting would re-plan from zero and the gap
                // prompt is already a targeted closure request.
                //
                // Cap = input.MaxCoverageIterations (default 2 for complex;
                // iterations are expensive here). MaxCoverageIterations=0 is
                // honored — the for-loop body never runs and we fall through.
                for (_coverageIteration = 1;
                     _coverageIteration <= input.MaxCoverageIterations;
                     _coverageIteration++)
                {
                    _pipelineStage = $"coverage-iteration-{_coverageIteration}";
                    await EmitStageAsync(input.SessionId, _pipelineStage);

                    var coverageInput = new CoverageInput(
                        OriginalPrompt: input.Prompt,
                        ContainerId: containerId,
                        WorkingDirectory: input.WorkspacePath,
                        MaxIterations: input.MaxCoverageIterations,
                        CurrentIteration: _coverageIteration,
                        ModelPower: 2,
                        AiAssistant: input.AiAssistant,
                        SessionId: input.SessionId);

                    var coverage = await Workflow.ExecuteActivityAsync(
                        (AiActivities a) => a.GradeCoverageAsync(coverageInput),
                        ActivityProfiles.Medium);

                    if (coverage.AllMet)
                        break;

                    // Direct agent call — same container, using the gap prompt.
                    var gapRunInput = new RunCliAgentInput(
                        Prompt: coverage.GapPrompt,
                        ContainerId: containerId,
                        AiAssistant: input.AiAssistant,
                        Model: triage.RecommendedModel,
                        ModelPower: triage.RecommendedModelPower,
                        WorkingDirectory: input.WorkspacePath,
                        SessionId: input.SessionId);

                    var gapRun = await Workflow.ExecuteActivityAsync(
                        (AiActivities a) => a.RunCliAgentAsync(gapRunInput),
                        ActivityProfiles.Long);

                    _totalCost += gapRun.CostUsd;
                    await EmitCostAsync(input.SessionId, _totalCost);
                }

                // After the coverage loop converges, hand the implementation off
                // to the reusable VerifyAndRepair child workflow so real
                // build/test/lint gates drive the final repair iterations
                // instead of GradeCoverage alone. The coverage loop is a
                // requirements-vs-output check; gates are a code-correctness
                // check — both are needed.
                if (Workflow.Patched("full-orchestrate-verify-and-repair-v1"))
                {
                    _pipelineStage = "verify-and-repair";
                    await EmitStageAsync(input.SessionId, "verify-and-repair");

                    var defaultGates = new[] { "compile", "test", "lint" };
                    var verifyInput = new VerifyAndRepairInput(
                        SessionId: input.SessionId,
                        ContainerId: containerId,
                        WorkingDirectory: input.WorkspacePath,
                        OriginalPrompt: input.Prompt,
                        AiAssistant: input.AiAssistant,
                        Model: triage.RecommendedModel,
                        ModelPower: triage.RecommendedModelPower,
                        Gates: input.SelectedGates ?? defaultGates,
                        WorkerOutput: $"Completed {complex.TaskCount} tasks",
                        MaxRepairAttempts: input.MaxRepairAttempts);

                    var verifyResult = await Workflow.ExecuteChildWorkflowAsync(
                        (VerifyAndRepairWorkflow w) => w.RunAsync(verifyInput),
                        new ChildWorkflowOptions { Id = $"{input.SessionId}-verify" });

                    _totalCost += verifyResult.RepairCostUsd;
                    await EmitCostAsync(input.SessionId, _totalCost);
                }

                result = new FullOrchestrateOutput(
                    PipelineUsed: "complex",
                    FinalResponse: $"Completed {complex.TaskCount} tasks",
                    TotalCostUsd: _totalCost);
            }
            else
            {
                _pipelineStage = "simple-path";
                await EmitStageAsync(input.SessionId, "simple-path");

                // Pass this workflow's container id down so SimpleAgent reuses
                // it instead of spawning a second container (which would
                // collide on noVNC port 6080 and abort the run).
                var simpleInput = new SimpleAgentInput(
                    SessionId: input.SessionId,
                    Prompt: finalPrompt,
                    AiAssistant: input.AiAssistant,
                    Model: triage.RecommendedModel,
                    ModelPower: triage.RecommendedModelPower,
                    WorkspacePath: input.WorkspacePath,
                    EnableGui: input.EnableGui,
                    ExistingContainerId: containerId);

                var simple = await Workflow.ExecuteChildWorkflowAsync(
                    (SimpleAgentWorkflow w) => w.RunAsync(simpleInput),
                    new ChildWorkflowOptions { Id = $"{input.SessionId}-simple" });

                _totalCost += simple.TotalCostUsd;
                await EmitCostAsync(input.SessionId, _totalCost);
                result = new FullOrchestrateOutput(
                    PipelineUsed: "simple",
                    FinalResponse: simple.Response,
                    TotalCostUsd: _totalCost);
            }

            _pipelineStage = "completed";
            await EmitStageAsync(input.SessionId, "completed");
            return result;
        }
        finally
        {
            // Only destroy what we spawned. When the container was handed in by
            // a parent (SmartImprove, etc.), the parent owns teardown. The
            // ownsContainer field captures that lifecycle decision once at
            // workflow entry and is read here — no second Patched() call needed
            // because the value is already deterministic per execution.
            if (ownsContainer)
            {
                _pipelineStage = "cleanup";
                await EmitStageAsync(input.SessionId, "cleanup");
                var destroyInput = new DestroyInput(containerId);
                await Workflow.ExecuteActivityAsync(
                    (DockerActivities a) => a.DestroyAsync(destroyInput),
                    ActivityProfiles.ContainerCleanup);
            }
            else
            {
                _pipelineStage = "completed-handoff";
                await EmitStageAsync(input.SessionId, "completed-handoff");
            }
        }
    }

    /// <summary>
    /// Emit a stage transition through the side-channel SignalR sink so the
    /// Studio chip moves through real stages mid-run. Gated on
    /// <c>Workflow.Patched("emit-stage-activity-v1")</c> so old workflow
    /// histories — which never scheduled this activity — replay deterministically.
    /// </summary>
    private static async Task EmitStageAsync(string sessionId, string stage)
    {
        if (!Workflow.Patched("emit-stage-activity-v1")) return;

        var stageInput = new EmitStageInput(sessionId, stage);
        await Workflow.ExecuteActivityAsync(
            (StageActivities a) => a.EmitStageAsync(stageInput),
            ActivityProfiles.Short);
    }

    /// <summary>
    /// Broadcast running total cost mid-run so the Studio cost tile updates
    /// live instead of only at completion. Gated on
    /// <c>Workflow.Patched("emit-cost-activity-v1")</c> for replay safety.
    /// </summary>
    private static async Task EmitCostAsync(string sessionId, decimal totalCost)
    {
        if (!Workflow.Patched("emit-cost-activity-v1")) return;

        var costInput = new EmitCostInput(sessionId, totalCost);
        await Workflow.ExecuteActivityAsync(
            (StageActivities a) => a.EmitCostAsync(costInput),
            ActivityProfiles.Short);
    }
}
