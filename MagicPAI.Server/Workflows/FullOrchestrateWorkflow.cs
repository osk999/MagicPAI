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
        _pipelineStage = "spawning-container";

        var spawnInput = new SpawnContainerInput(
            SessionId: input.SessionId,
            WorkspacePath: input.WorkspacePath,
            EnableGui: input.EnableGui);

        var spawn = await Workflow.ExecuteActivityAsync(
            (DockerActivities a) => a.SpawnAsync(spawnInput),
            ActivityProfiles.Container);

        try
        {
            // Stage 1 — website classification gate.
            _pipelineStage = "classifying-website";

            var classifyInput = new WebsiteClassifyInput(
                Prompt: input.Prompt,
                ContainerId: spawn.ContainerId,
                AiAssistant: input.AiAssistant,
                SessionId: input.SessionId);

            var websiteClass = await Workflow.ExecuteActivityAsync(
                (AiActivities a) => a.ClassifyWebsiteTaskAsync(classifyInput),
                ActivityProfiles.Medium);

            if (websiteClass.IsWebsiteTask)
            {
                _pipelineStage = "website-audit";

                var siteInput = new WebsiteAuditInput(
                    SessionId: input.SessionId,
                    ContainerId: spawn.ContainerId,
                    Prompt: input.Prompt,
                    WorkspacePath: input.WorkspacePath,
                    AiAssistant: input.AiAssistant,
                    Model: input.Model);

                var siteResult = await Workflow.ExecuteChildWorkflowAsync(
                    (WebsiteAuditLoopWorkflow w) => w.RunAsync(siteInput),
                    new ChildWorkflowOptions { Id = $"{input.SessionId}-website" });

                _totalCost += siteResult.CostUsd;
                _pipelineStage = "completed";

                return new FullOrchestrateOutput(
                    PipelineUsed: "website-audit",
                    FinalResponse: siteResult.Summary,
                    TotalCostUsd: _totalCost);
            }

            // Stage 2 — research the prompt for grounding.
            _pipelineStage = "research-prompt";

            var researchInput = new ResearchPromptInput(
                Prompt: input.Prompt,
                AiAssistant: input.AiAssistant,
                ContainerId: spawn.ContainerId,
                ModelPower: 2,
                SessionId: input.SessionId);

            var research = await Workflow.ExecuteActivityAsync(
                (AiActivities a) => a.ResearchPromptAsync(researchInput),
                ActivityProfiles.Long);

            // Stage 3 — triage to decide complexity.
            _pipelineStage = "triage";

            var triageInput = new TriageInput(
                Prompt: research.EnhancedPrompt,
                ContainerId: spawn.ContainerId,
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
                    return new FullOrchestrateOutput(
                        PipelineUsed: "rejected",
                        FinalResponse: $"Gate rejected: {_gateRejectReason}",
                        TotalCostUsd: _totalCost);
                }
            }

            // An InjectPrompt signal received at any point before now overrides
            // the research-enhanced prompt for the downstream branch.
            var finalPrompt = _injectedPrompt ?? research.EnhancedPrompt;

            FullOrchestrateOutput result;
            if (triage.IsComplex)
            {
                _pipelineStage = "complex-path";

                var complexInput = new OrchestrateComplexInput(
                    SessionId: input.SessionId,
                    Prompt: finalPrompt,
                    ContainerId: spawn.ContainerId,
                    WorkspacePath: input.WorkspacePath,
                    AiAssistant: input.AiAssistant,
                    Model: triage.RecommendedModel,
                    ModelPower: triage.RecommendedModelPower);

                var complex = await Workflow.ExecuteChildWorkflowAsync(
                    (OrchestrateComplexPathWorkflow w) => w.RunAsync(complexInput),
                    new ChildWorkflowOptions { Id = $"{input.SessionId}-complex" });

                _totalCost += complex.TotalCostUsd;

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

                    var coverageInput = new CoverageInput(
                        OriginalPrompt: input.Prompt,
                        ContainerId: spawn.ContainerId,
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
                        ContainerId: spawn.ContainerId,
                        AiAssistant: input.AiAssistant,
                        Model: triage.RecommendedModel,
                        ModelPower: triage.RecommendedModelPower,
                        WorkingDirectory: input.WorkspacePath,
                        SessionId: input.SessionId);

                    var gapRun = await Workflow.ExecuteActivityAsync(
                        (AiActivities a) => a.RunCliAgentAsync(gapRunInput),
                        ActivityProfiles.Long);

                    _totalCost += gapRun.CostUsd;
                }

                result = new FullOrchestrateOutput(
                    PipelineUsed: "complex",
                    FinalResponse: $"Completed {complex.TaskCount} tasks",
                    TotalCostUsd: _totalCost);
            }
            else
            {
                _pipelineStage = "simple-path";

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
                    ExistingContainerId: spawn.ContainerId);

                var simple = await Workflow.ExecuteChildWorkflowAsync(
                    (SimpleAgentWorkflow w) => w.RunAsync(simpleInput),
                    new ChildWorkflowOptions { Id = $"{input.SessionId}-simple" });

                _totalCost += simple.TotalCostUsd;
                result = new FullOrchestrateOutput(
                    PipelineUsed: "simple",
                    FinalResponse: simple.Response,
                    TotalCostUsd: _totalCost);
            }

            _pipelineStage = "completed";
            return result;
        }
        finally
        {
            _pipelineStage = "cleanup";
            var destroyInput = new DestroyInput(spawn.ContainerId);
            await Workflow.ExecuteActivityAsync(
                (DockerActivities a) => a.DestroyAsync(destroyInput),
                ActivityProfiles.ContainerCleanup);
        }
    }
}
