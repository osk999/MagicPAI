using Elsa.Common.Models;
using Elsa.Workflows.Models;
using Elsa.Workflows.Management;
using Elsa.Workflows.Runtime;
using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Requests;
using MagicPAI.Core.Models;
using MagicPAI.Core.Services;
using MagicPAI.Server.Bridge;
using MagicPAI.Server.Hubs;
using MagicPAI.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

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
    private readonly IWorkflowDefinitionService _workflowDefinitionService;
    private readonly IWorkflowCancellationDispatcher _cancellationDispatcher;
    private readonly SessionTracker _tracker;
    private readonly SessionHistoryReader _historyReader;
    private readonly IHubContext<SessionHub> _hubContext;
    private readonly SessionLaunchPlanner _launchPlanner;
    private readonly WorkflowPublisher _workflowPublisher;
    private readonly ILogger<SessionController> _logger;

    public SessionController(
        IWorkflowDispatcher dispatcher,
        IWorkflowDefinitionService workflowDefinitionService,
        IWorkflowCancellationDispatcher cancellationDispatcher,
        SessionTracker tracker,
        SessionHistoryReader historyReader,
        IHubContext<SessionHub> hubContext,
        SessionLaunchPlanner launchPlanner,
        WorkflowPublisher workflowPublisher,
        ILogger<SessionController> logger)
    {
        _dispatcher = dispatcher;
        _workflowDefinitionService = workflowDefinitionService;
        _cancellationDispatcher = cancellationDispatcher;
        _tracker = tracker;
        _historyReader = historyReader;
        _hubContext = hubContext;
        _launchPlanner = launchPlanner;
        _workflowPublisher = workflowPublisher;
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

        await _workflowPublisher.InitializeAsync(HttpContext.RequestAborted);

        var launch = _launchPlanner.Plan(
            request.Prompt,
            request.WorkspacePath,
            request.AiAssistant,
            request.Agent,
            request.Model,
            request.ModelPower ?? 0,
            request.StructuredOutputSchema,
            request.WorkflowName ?? "full-orchestrate");
        var correlationId = Guid.NewGuid().ToString("N");
        var instanceId = Guid.NewGuid().ToString("N");
        var sessionInfo = new SessionInfo
        {
            Id = instanceId,
            WorkflowId = launch.RequestedWorkflowName,
            State = "running",
            Agent = launch.ResolvedAssistant,
            PromptPreview = request.Prompt.Length > 100
                ? request.Prompt[..100] + "..."
                : request.Prompt,
            CreatedAt = DateTime.UtcNow
        };
        _tracker.RegisterSession(instanceId, sessionInfo);

        try
        {
            await DispatchWorkflowAsync(instanceId, launch.DefinitionId, correlationId, launch.Input, HttpContext.RequestAborted);
        }
        catch
        {
            _tracker.RemoveSession(instanceId);
            throw;
        }

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
    public async Task<ActionResult<SessionInfo>> GetSession(string id)
    {
        var session = await _historyReader.GetSessionAsync(
            id,
            _tracker.GetSession(id),
            HttpContext.RequestAborted);
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
    public async Task<ActionResult<string[]>> GetOutput(string id)
    {
        var session = await _historyReader.GetSessionAsync(
            id,
            _tracker.GetSession(id),
            HttpContext.RequestAborted);
        if (session is null)
            return NotFound(new { Message = $"Session {id} not found" });

        return Ok(await _historyReader.GetOutputAsync(
            id,
            _tracker.GetOutput(id),
            HttpContext.RequestAborted));
    }

    /// <summary>
    /// Get tracked activity states for a session.
    /// </summary>
    [HttpGet("{id}/activities")]
    public async Task<ActionResult<IReadOnlyList<ActivityState>>> GetActivities(string id)
    {
        var session = await _historyReader.GetSessionAsync(
            id,
            _tracker.GetSession(id),
            HttpContext.RequestAborted);
        if (session is null)
            return NotFound(new { Message = $"Session {id} not found" });

        return Ok(await _historyReader.GetActivitiesAsync(
            id,
            _tracker.GetActivities(id),
            HttpContext.RequestAborted));
    }

    /// <summary>
    /// Get tracked workflow insights for a session.
    /// </summary>
    [HttpGet("{id}/insights")]
    public async Task<ActionResult<IReadOnlyList<TaskInsightEvent>>> GetInsights(string id)
    {
        var session = await _historyReader.GetSessionAsync(
            id,
            _tracker.GetSession(id),
            HttpContext.RequestAborted);
        if (session is null)
            return NotFound(new { Message = $"Session {id} not found" });

        return Ok(_tracker.GetInsights(id));
    }

    private async Task<string> DispatchWorkflowAsync(
        string instanceId,
        string definitionId,
        string correlationId,
        Dictionary<string, object> input,
        CancellationToken cancellationToken)
    {
        var definitionVersionId = await ResolveDefinitionVersionIdAsync(definitionId, cancellationToken);
        var request = new DispatchWorkflowDefinitionRequest
        {
            DefinitionVersionId = definitionVersionId,
            InstanceId = instanceId,
            CorrelationId = correlationId,
            Input = input
        };

        var response = await _dispatcher.DispatchAsync(request, cancellationToken);
        response.ThrowIfFailed();
        _logger.LogInformation(
            "Dispatched workflow {DefinitionId} as instance {WorkflowInstanceId}",
            definitionId,
            instanceId);
        return instanceId;
    }

    private async Task<string> ResolveDefinitionVersionIdAsync(string definitionId, CancellationToken cancellationToken)
    {
        // Always resolve the REAL published definitionVersionId from the store.
        // Fabricating "{id}:v1" means Studio's /workflow-instances list can't hydrate
        // the definition lookup and the whole Instances page renders as empty.
        var definition = await _workflowDefinitionService.FindWorkflowDefinitionAsync(
            WorkflowDefinitionHandle.ByDefinitionId(definitionId, VersionOptions.Published),
            cancellationToken);
        return definition?.Id
            ?? throw new InvalidOperationException(
                $"No published workflow definition found for '{definitionId}'.");
    }

}

// --- Request/Response DTOs ---

public record CreateSessionRequest(
    string Prompt,
    string? WorkspacePath = null,
    string? AiAssistant = null,
    string? Agent = null,
    string? Model = null,
    int? ModelPower = null,
    string? StructuredOutputSchema = null,
    string? WorkflowName = null);

public record CreateSessionResponse(string SessionId, string? WorkflowId);

public record ApprovalRequest(bool Approved);
