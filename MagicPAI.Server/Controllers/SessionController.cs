// MagicPAI.Server/Controllers/SessionController.cs
// Temporal-unified session API per temporal.md §M.4 / §9.3. Dispatches every
// workflow type via ITemporalClient.StartWorkflowAsync using strongly-typed
// inputs built by SessionLaunchPlanner. Coexists with Elsa services
// (IHostedService WorkflowPublisher, notification handlers) until Phase 3.
using MagicPAI.Server.Bridge;
using MagicPAI.Server.Services;
using MagicPAI.Server.Workflows;
using MagicPAI.Workflows.Contracts;
using Microsoft.AspNetCore.Mvc;
using Temporalio.Client;
using Temporalio.Common;

namespace MagicPAI.Server.Controllers;

[ApiController]
[Route("api/sessions")]
public class SessionController : ControllerBase
{
    private readonly ITemporalClient _temporal;
    private readonly WorkflowCatalog _catalog;
    private readonly SessionLaunchPlanner _planner;
    private readonly SessionTracker _tracker;
    private readonly SessionHistoryReader _history;
    private readonly MagicPaiMetrics _metrics;
    private readonly ILogger<SessionController> _log;

    public SessionController(
        ITemporalClient temporal,
        WorkflowCatalog catalog,
        SessionLaunchPlanner planner,
        SessionTracker tracker,
        SessionHistoryReader history,
        MagicPaiMetrics metrics,
        ILogger<SessionController> log)
    {
        _temporal = temporal;
        _catalog = catalog;
        _planner = planner;
        _tracker = tracker;
        _history = history;
        _metrics = metrics;
        _log = log;
    }

    /// <summary>
    /// Start a Temporal workflow for the given request. Returns the workflow
    /// id (used as session id) in an <see cref="AcceptedResult"/>.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateSessionRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Prompt))
            return BadRequest(new { Message = "Prompt is required" });
        if (string.IsNullOrWhiteSpace(req.WorkflowType))
            return BadRequest(new { Message = "WorkflowType is required" });

        SessionLaunchPlan plan;
        try
        {
            plan = _planner.Plan(req);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { Message = ex.Message });
        }

        var workflowId = $"mpai-{Guid.NewGuid():N}";
        var opts = new WorkflowOptions(workflowId, taskQueue: WorkflowCatalog.DefaultTaskQueue)
        {
            TaskTimeout = TimeSpan.FromMinutes(1),
            TypedSearchAttributes = new SearchAttributeCollection.Builder()
                .Set(SearchAttributeKey.CreateText("MagicPaiAiAssistant"), plan.AiAssistant)
                .Set(SearchAttributeKey.CreateText("MagicPaiWorkflowType"), plan.WorkflowType)
                .Set(SearchAttributeKey.CreateText("MagicPaiSessionKind"), plan.SessionKind)
                .ToSearchAttributeCollection()
        };

        try
        {
            await StartWorkflowAsync(plan, workflowId, opts);
        }
        catch (Temporalio.Exceptions.RpcException ex)
        {
            _log.LogError(ex, "Temporal dispatch failed for workflow {Workflow}", plan.WorkflowType);
            return StatusCode(StatusCodes.Status502BadGateway,
                new { Message = $"Failed to dispatch workflow: {ex.Message}" });
        }

        _tracker.RegisterSession(workflowId, new SessionInfo
        {
            Id = workflowId,
            WorkflowId = plan.WorkflowType,
            State = "running",
            Agent = plan.AiAssistant,
            PromptPreview = req.Prompt.Length > 100
                ? req.Prompt[..100] + "..."
                : req.Prompt,
            CreatedAt = DateTime.UtcNow
        });

        _metrics.SessionsStarted.Add(
            1,
            new KeyValuePair<string, object?>("workflow_type", plan.WorkflowType),
            new KeyValuePair<string, object?>("ai_assistant", plan.AiAssistant));

        _log.LogInformation(
            "Session {Id} started; workflow type={Type} assistant={Assistant}",
            workflowId, plan.WorkflowType, plan.AiAssistant);

        return Accepted(
            $"/api/sessions/{workflowId}",
            new CreateSessionResponse(workflowId, plan.WorkflowType));
    }

    /// <summary>List recent sessions via Temporal visibility.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int take = 100, CancellationToken ct = default)
    {
        var sessions = new List<SessionSummary>();
        try
        {
            await foreach (var s in _history.ListRecentAsync(TimeSpan.FromDays(7), take, ct))
                sessions.Add(s);
        }
        catch (Exception ex) when (ex is Temporalio.Exceptions.RpcException or HttpRequestException)
        {
            _log.LogWarning(ex, "Temporal visibility unavailable; falling back to tracker");
        }

        if (sessions.Count == 0)
        {
            // Fall back to the in-memory tracker for local/offline debug flows.
            foreach (var info in _tracker.GetAllSessions())
                sessions.Add(new SessionSummary(
                    SessionId: info.Id,
                    WorkflowType: info.WorkflowId ?? "unknown",
                    Status: info.State ?? "unknown",
                    StartTime: info.CreatedAt,
                    CloseTime: null,
                    AiAssistant: info.Agent ?? "",
                    TotalCostUsd: info.TotalCostUsd));
        }

        return Ok(sessions);
    }

    /// <summary>Get a single session's current state via DescribeAsync.</summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        try
        {
            var h = _temporal.GetWorkflowHandle(id);
            var desc = await h.DescribeAsync(
                new WorkflowDescribeOptions { Rpc = new RpcOptions { CancellationToken = ct } });
            return Ok(new
            {
                SessionId = id,
                Status = desc.Status.ToString(),
                WorkflowType = desc.WorkflowType,
                StartTime = desc.StartTime,
                CloseTime = desc.CloseTime,
                RunId = desc.RunId,
                TaskQueue = desc.TaskQueue
            });
        }
        catch (Temporalio.Exceptions.RpcException ex)
            when (ex.Code == Temporalio.Exceptions.RpcException.StatusCode.NotFound)
        {
            return NotFound(new { SessionId = id, Message = "Session not found" });
        }
    }

    /// <summary>Request graceful cancellation.</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Cancel(string id, CancellationToken ct)
    {
        try
        {
            var h = _temporal.GetWorkflowHandle(id);
            await h.CancelAsync(new WorkflowCancelOptions
            {
                Rpc = new RpcOptions { CancellationToken = ct }
            });
            _tracker.UpdateState(id, "cancelled");
            return NoContent();
        }
        catch (Temporalio.Exceptions.RpcException ex)
            when (ex.Code == Temporalio.Exceptions.RpcException.StatusCode.NotFound)
        {
            return NotFound(new { SessionId = id, Message = "Session not found" });
        }
    }

    /// <summary>Force-terminate a workflow.</summary>
    [HttpPost("{id}/terminate")]
    public async Task<IActionResult> Terminate(
        string id, [FromBody] TerminateRequest req, CancellationToken ct)
    {
        try
        {
            var h = _temporal.GetWorkflowHandle(id);
            await h.TerminateAsync(
                reason: req?.Reason ?? "Force terminated from API",
                new WorkflowTerminateOptions
                {
                    Rpc = new RpcOptions { CancellationToken = ct }
                });
            _tracker.UpdateState(id, "terminated");
            return NoContent();
        }
        catch (Temporalio.Exceptions.RpcException ex)
            when (ex.Code == Temporalio.Exceptions.RpcException.StatusCode.NotFound)
        {
            return NotFound(new { SessionId = id, Message = "Session not found" });
        }
    }

    /// <summary>Resume a paused workflow via ApproveGate / RejectGate signals.</summary>
    [HttpPost("{id}/approve")]
    public async Task<IActionResult> Approve(
        string id, [FromBody] ApprovalRequest req, CancellationToken ct)
    {
        try
        {
            var h = _temporal.GetWorkflowHandle(id);
            if (req.Approved)
                await h.SignalAsync(
                    "ApproveGate",
                    new object[] { "api", req.Comment ?? "" },
                    new WorkflowSignalOptions { Rpc = new RpcOptions { CancellationToken = ct } });
            else
                await h.SignalAsync(
                    "RejectGate",
                    new object[] { req.Comment ?? "rejected" },
                    new WorkflowSignalOptions { Rpc = new RpcOptions { CancellationToken = ct } });

            return Ok(new { Message = $"Approval processed for session {id}", Approved = req.Approved });
        }
        catch (Temporalio.Exceptions.RpcException ex)
            when (ex.Code == Temporalio.Exceptions.RpcException.StatusCode.NotFound)
        {
            return NotFound(new { SessionId = id, Message = "Session not found" });
        }
    }

    /// <summary>Expose the Temporal workflow catalog for Studio form rendering.</summary>
    [HttpGet("/api/workflows")]
    public IActionResult ListWorkflows() =>
        Ok(_catalog.UserVisible.Select(e => new
        {
            e.WorkflowTypeName,
            e.DisplayName,
            e.Description,
            e.Category,
            e.SortOrder,
            e.RequiresAiAssistant,
            e.SupportedModels
        }));

    // ───────────────────────────────────────────────────────────────────────
    // Internal helpers
    // ───────────────────────────────────────────────────────────────────────

    private async Task<WorkflowHandle> StartWorkflowAsync(
        SessionLaunchPlan plan, string workflowId, WorkflowOptions opts) =>
        plan.WorkflowType switch
        {
            "SimpleAgent" => await _temporal.StartWorkflowAsync(
                (SimpleAgentWorkflow wf) => wf.RunAsync(_planner.AsSimpleAgentInput(plan, workflowId)),
                opts),
            "FullOrchestrate" => await _temporal.StartWorkflowAsync(
                (FullOrchestrateWorkflow wf) => wf.RunAsync(_planner.AsFullOrchestrateInput(plan, workflowId)),
                opts),
            "DeepResearchOrchestrate" => await _temporal.StartWorkflowAsync(
                (DeepResearchOrchestrateWorkflow wf) => wf.RunAsync(_planner.AsDeepResearchInput(plan, workflowId)),
                opts),
            "StandardOrchestrate" => await _temporal.StartWorkflowAsync(
                (StandardOrchestrateWorkflow wf) => wf.RunAsync(_planner.AsStandardInput(plan, workflowId)),
                opts),
            "OrchestrateSimplePath" => await _temporal.StartWorkflowAsync(
                (OrchestrateSimplePathWorkflow wf) => wf.RunAsync(_planner.AsOrchestrateSimplePathInput(plan, workflowId)),
                opts),
            "OrchestrateComplexPath" => await _temporal.StartWorkflowAsync(
                (OrchestrateComplexPathWorkflow wf) => wf.RunAsync(_planner.AsOrchestrateComplexPathInput(plan, workflowId)),
                opts),
            "PromptEnhancer" => await _temporal.StartWorkflowAsync(
                (PromptEnhancerWorkflow wf) => wf.RunAsync(_planner.AsPromptEnhancerInput(plan, workflowId)),
                opts),
            "ContextGatherer" => await _temporal.StartWorkflowAsync(
                (ContextGathererWorkflow wf) => wf.RunAsync(_planner.AsContextGathererInput(plan, workflowId)),
                opts),
            "PromptGrounding" => await _temporal.StartWorkflowAsync(
                (PromptGroundingWorkflow wf) => wf.RunAsync(_planner.AsPromptGroundingInput(plan, workflowId)),
                opts),
            "ResearchPipeline" => await _temporal.StartWorkflowAsync(
                (ResearchPipelineWorkflow wf) => wf.RunAsync(_planner.AsResearchPipelineInput(plan, workflowId)),
                opts),
            "PostExecutionPipeline" => await _temporal.StartWorkflowAsync(
                (PostExecutionPipelineWorkflow wf) => wf.RunAsync(_planner.AsPostExecInput(plan, workflowId)),
                opts),
            "WebsiteAuditCore" => await _temporal.StartWorkflowAsync(
                (WebsiteAuditCoreWorkflow wf) => wf.RunAsync(_planner.AsWebsiteCoreInput(plan, workflowId)),
                opts),
            "WebsiteAuditLoop" => await _temporal.StartWorkflowAsync(
                (WebsiteAuditLoopWorkflow wf) => wf.RunAsync(_planner.AsWebsiteLoopInput(plan, workflowId)),
                opts),
            "VerifyAndRepair" => await _temporal.StartWorkflowAsync(
                (VerifyAndRepairWorkflow wf) => wf.RunAsync(_planner.AsVerifyRepairInput(plan, workflowId)),
                opts),
            "ClawEvalAgent" => await _temporal.StartWorkflowAsync(
                (ClawEvalAgentWorkflow wf) => wf.RunAsync(_planner.AsClawEvalInput(plan, workflowId)),
                opts),
            "ComplexTaskWorker" => await _temporal.StartWorkflowAsync(
                (ComplexTaskWorkerWorkflow wf) => wf.RunAsync(_planner.AsComplexTaskWorkerInput(plan, workflowId)),
                opts),
            "IterativeLoop" => await _temporal.StartWorkflowAsync(
                (IterativeLoopWorkflow wf) => wf.RunAsync(_planner.AsIterativeLoopInput(plan, workflowId)),
                opts),
            _ => throw new ArgumentException($"Unknown workflow type: {plan.WorkflowType}")
        };

}

// ───────────────────────────────────────────────────────────────────────────
// Request / Response records
// ───────────────────────────────────────────────────────────────────────────

/// <summary>
/// Generic request for creating a session. Workflow-specific optional
/// parameters (SectionId, EvalTaskId, Gates, etc.) are carried here so the
/// typed planner converters can project them into the appropriate input
/// record. Null when not applicable.
/// </summary>
public record CreateSessionRequest(
    string Prompt,
    string WorkflowType = "FullOrchestrate",
    string? AiAssistant = null,
    string? Model = null,
    int ModelPower = 0,
    string? WorkspacePath = null,
    bool? EnableGui = null,

    // Workflow-specific passthroughs ────────────────────────────────────────
    string? SectionId = null,
    IReadOnlyList<string>? SectionIds = null,
    string? EvalTaskId = null,
    string? TaskId = null,
    IReadOnlyList<string>? DependsOn = null,
    IReadOnlyList<string>? FilesTouched = null,
    string? ParentSessionId = null,
    string? ContainerId = null,
    string? WorkerOutput = null,
    IReadOnlyList<string>? Gates = null,
    int? MaxRepairAttempts = null,
    // FullOrchestrate HITL gate passthrough — when true, the workflow parks
    // after triage and awaits an ApproveGate/RejectGate signal.
    bool? RequireTriageApproval = null,
    int? GateApprovalTimeoutHours = null,
    // Triage complexity threshold (1–10) — lower values force more prompts
    // down the complex-path branch (decomposition). Default 7.
    int? ComplexityThreshold = null,
    // SimpleAgent coverage-fast-path — skip GradeCoverage call when all gates
    // pass on first verify. Saves ~5-10s per successful run.
    bool? SkipCoverageWhenGatesPass = null,
    // IterativeLoop passthroughs
    int? MinIterations = null,
    int? MaxIterations = null,
    string? CompletionStrategy = null,     // "marker" | "classifier" | "structured"
    string? CompletionMarker = null,       // default "[DONE]"
    string? CompletionInstructions = null, // classifier-strategy hint
    decimal? MaxBudgetUsd = null,
    Dictionary<string, string>? CustomParams = null);

public record CreateSessionResponse(string SessionId, string WorkflowType);

public record ApprovalRequest(bool Approved, string? Comment = null);

public record TerminateRequest(string? Reason);
