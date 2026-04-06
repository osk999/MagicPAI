using Elsa.Workflows.Runtime;
using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Parameters;
using Elsa.Workflows.Runtime.Requests;
using MagicPAI.Core.Models;
using MagicPAI.Server.Bridge;
using MagicPAI.Server.Hubs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace MagicPAI.Server.Controllers;

/// <summary>
/// REST API for session management. Provides endpoints for creating, listing,
/// querying, stopping, and approving sessions.
/// </summary>
[ApiController]
[Route("api/sessions")]
public class SessionController : ControllerBase
{
    private readonly IWorkflowDispatcher _dispatcher;
    private readonly IWorkflowRuntime _runtime;
    private readonly IWorkflowCancellationDispatcher _cancellationDispatcher;
    private readonly SessionTracker _tracker;
    private readonly IHubContext<SessionHub> _hubContext;
    private readonly ILogger<SessionController> _logger;

    public SessionController(
        IWorkflowDispatcher dispatcher,
        IWorkflowRuntime runtime,
        IWorkflowCancellationDispatcher cancellationDispatcher,
        SessionTracker tracker,
        IHubContext<SessionHub> hubContext,
        ILogger<SessionController> logger)
    {
        _dispatcher = dispatcher;
        _runtime = runtime;
        _cancellationDispatcher = cancellationDispatcher;
        _tracker = tracker;
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <summary>
    /// Create a new session by dispatching an Elsa workflow.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<CreateSessionResponse>> CreateSession(
        [FromBody] CreateSessionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
            return BadRequest(new { Message = "Prompt is required" });

        var instanceId = Guid.NewGuid().ToString("N");

        var definitionId = WorkflowPublisher.ResolveDefinitionId(
            request.WorkflowName ?? "full-orchestrate");

        // Use IWorkflowRuntime which builds workflow from CLR code directly,
        // preserving flowchart activities and connections properly
        var runtimeParams = new StartWorkflowRuntimeParams
        {
            InstanceId = instanceId,
            Input = new Dictionary<string, object>
            {
                ["Prompt"] = request.Prompt,
                ["WorkspacePath"] = request.WorkspacePath ?? "",
                ["Agent"] = request.Agent ?? "claude",
                ["Model"] = request.Model ?? "sonnet"
            },
            CorrelationId = instanceId,
        };

        var startResult = await _runtime.TryStartWorkflowAsync(definitionId, runtimeParams);
        if (startResult is null)
            return NotFound(new { Message = $"Workflow '{request.WorkflowName}' could not start" });

        var sessionInfo = new SessionInfo
        {
            Id = instanceId,
            WorkflowId = request.WorkflowName ?? "full-orchestrate",
            State = "running",
            Agent = request.Agent ?? "claude",
            PromptPreview = request.Prompt.Length > 100
                ? request.Prompt[..100] + "..."
                : request.Prompt,
            CreatedAt = DateTime.UtcNow
        };

        _tracker.RegisterSession(instanceId, sessionInfo);

        _logger.LogInformation("Created session {SessionId}", instanceId);

        return Ok(new CreateSessionResponse(instanceId, sessionInfo.WorkflowId));
    }

    /// <summary>
    /// List all sessions.
    /// </summary>
    [HttpGet]
    public ActionResult<IReadOnlyCollection<SessionInfo>> ListSessions()
    {
        return Ok(_tracker.GetAllSessions());
    }

    /// <summary>
    /// Get a specific session's state and info.
    /// </summary>
    [HttpGet("{id}")]
    public ActionResult<SessionInfo> GetSession(string id)
    {
        var session = _tracker.GetSession(id);
        if (session is null)
            return NotFound(new { Message = $"Session {id} not found" });

        return Ok(session);
    }

    /// <summary>
    /// Stop a running session by cancelling its workflow.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<ActionResult> StopSession(string id)
    {
        var session = _tracker.GetSession(id);
        if (session is null)
            return NotFound(new { Message = $"Session {id} not found" });

        var cancelRequest = new DispatchCancelWorkflowRequest
        {
            WorkflowInstanceId = id
        };

        await _cancellationDispatcher.DispatchAsync(cancelRequest, CancellationToken.None);
        _tracker.UpdateState(id, "cancelled");

        await _hubContext.Clients.Group(id).SendAsync("sessionStateChanged",
            new SessionStateEvent(id, "cancelled"));

        _logger.LogInformation("Stopped session {SessionId}", id);

        return Ok(new { Message = $"Session {id} cancelled" });
    }

    /// <summary>
    /// Approve or reject a human-in-the-loop gate.
    /// </summary>
    [HttpPost("{id}/approve")]
    public async Task<ActionResult> Approve(string id, [FromBody] ApprovalRequest request)
    {
        var session = _tracker.GetSession(id);
        if (session is null)
            return NotFound(new { Message = $"Session {id} not found" });

        var resumeRequest = new DispatchWorkflowInstanceRequest(id)
        {
            BookmarkId = null, // Will resume the first available bookmark
            Input = new Dictionary<string, object>
            {
                ["Decision"] = request.Approved ? "approve" : "reject"
            }
        };

        await _dispatcher.DispatchAsync(resumeRequest, CancellationToken.None);

        _logger.LogInformation(
            "Approval for session {SessionId}: {Decision}", id, request.Approved);

        return Ok(new { Message = $"Approval processed for session {id}" });
    }

    /// <summary>
    /// Get buffered output for a session.
    /// </summary>
    [HttpGet("{id}/output")]
    public ActionResult<string[]> GetOutput(string id)
    {
        var session = _tracker.GetSession(id);
        if (session is null)
            return NotFound(new { Message = $"Session {id} not found" });

        return Ok(_tracker.GetOutput(id));
    }
}

// --- Request/Response DTOs ---

public record CreateSessionRequest(
    string Prompt,
    string? WorkspacePath = null,
    string? Agent = null,
    string? Model = null,
    string? WorkflowName = null);

public record CreateSessionResponse(string SessionId, string? WorkflowId);

public record ApprovalRequest(bool Approved);
