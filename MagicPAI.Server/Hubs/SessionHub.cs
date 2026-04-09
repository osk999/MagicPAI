using System.Collections.Concurrent;
using Elsa.Common.Models;
using Elsa.Workflows.Models;
using Elsa.Workflows.Management;
using Elsa.Workflows.Runtime;
using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Requests;
using MagicPAI.Core.Models;
using MagicPAI.Core.Services;
using MagicPAI.Server.Bridge;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;


namespace MagicPAI.Server.Hubs;

/// <summary>
/// SignalR hub for real-time session management.
/// Clients connect here to create sessions, receive streaming output,
/// approve/reject human-in-the-loop gates, and monitor workflow progress.
/// </summary>
public class SessionHub : Hub
{
    private readonly IWorkflowDispatcher _dispatcher;
    private readonly IWorkflowDefinitionService _workflowDefinitionService;
    private readonly IWorkflowCancellationDispatcher _cancellationDispatcher;
    private readonly SessionTracker _tracker;
    private readonly ILogger<SessionHub> _logger;

    // Map connectionId -> sessionIds for cleanup on disconnect
    private static readonly ConcurrentDictionary<string, HashSet<string>> ConnectionSessions = new();

    public SessionHub(
        IWorkflowDispatcher dispatcher,
        IWorkflowDefinitionService workflowDefinitionService,
        IWorkflowCancellationDispatcher cancellationDispatcher,
        SessionTracker tracker,
        ILogger<SessionHub> logger)
    {
        _dispatcher = dispatcher;
        _workflowDefinitionService = workflowDefinitionService;
        _cancellationDispatcher = cancellationDispatcher;
        _tracker = tracker;
        _logger = logger;
    }

    /// <summary>
    /// Create a new session by dispatching an Elsa workflow.
    /// Returns the workflow instance ID which serves as the session ID.
    /// </summary>
    public async Task<CreateSessionResult> CreateSession(
        string prompt,
        string workspacePath,
        string agent = "claude",
        string model = "auto",
        int modelPower = 0,
        string aiAssistant = "",
        string structuredOutputSchema = "",
        string workflowName = "full-orchestrate")
    {
        var resolvedAssistant = AiAssistantResolver.NormalizeAssistant(
            string.IsNullOrWhiteSpace(aiAssistant) ? agent : aiAssistant,
            "claude");
        var addedToGroup = false;
        string? instanceId = null;

        try
        {
            var definitionId = WorkflowPublisher.ResolveDefinitionId(workflowName);
            instanceId = Guid.NewGuid().ToString("N");
            var input = BuildWorkflowInput(
                prompt,
                workspacePath,
                resolvedAssistant,
                model,
                modelPower,
                structuredOutputSchema);
            var correlationId = Guid.NewGuid().ToString("N");
            var definitionVersionId = await ResolveDefinitionVersionIdAsync(definitionId, CancellationToken.None);
            var dispatchRequest = new DispatchWorkflowDefinitionRequest
            {
                DefinitionVersionId = definitionVersionId,
                InstanceId = instanceId,
                CorrelationId = correlationId,
                Input = input
            };
            var dispatchResponse = await _dispatcher.DispatchAsync(dispatchRequest, CancellationToken.None);
            dispatchResponse.ThrowIfFailed();

            var sessionInfo = new SessionInfo
            {
                Id = instanceId,
                WorkflowId = workflowName,
                State = "running",
                Agent = resolvedAssistant,
                PromptPreview = prompt.Length > 100 ? prompt[..100] + "..." : prompt,
                CreatedAt = DateTime.UtcNow
            };

            _tracker.RegisterSession(instanceId, sessionInfo);
            var sessions = ConnectionSessions.GetOrAdd(Context.ConnectionId, _ => new HashSet<string>());
            lock (sessions) { sessions.Add(instanceId); }

            await Groups.AddToGroupAsync(Context.ConnectionId, instanceId);
            addedToGroup = true;

            _logger.LogInformation(
                "Session {SessionId} created for workflow {Workflow} by connection {ConnectionId}",
                instanceId, workflowName, Context.ConnectionId);

            return new CreateSessionResult(instanceId, workflowName);
        }
        catch (Exception ex)
        {
            if (!string.IsNullOrWhiteSpace(instanceId))
            {
                _tracker.RemoveSession(instanceId);

                if (ConnectionSessions.TryGetValue(Context.ConnectionId, out var connectionSessions))
                    lock (connectionSessions) { connectionSessions.Remove(instanceId); }

                if (addedToGroup)
                    await Groups.RemoveFromGroupAsync(Context.ConnectionId, instanceId);
            }

            _logger.LogError(ex,
                "Failed to create session from hub. Workflow={Workflow} ConnectionId={ConnectionId}",
                workflowName, Context.ConnectionId);
            throw;
        }
    }

    /// <summary>
    /// Stop a running session by cancelling its workflow instance.
    /// </summary>
    public async Task StopSession(string sessionId)
    {
        _logger.LogInformation("Stopping session {SessionId}", sessionId);

        var cancelRequest = new DispatchCancelWorkflowRequest
        {
            WorkflowInstanceId = sessionId
        };

        await _cancellationDispatcher.DispatchAsync(cancelRequest, CancellationToken.None);
        _tracker.UpdateState(sessionId, "cancelled");

        await Clients.Group(sessionId).SendAsync("sessionStateChanged",
            new SessionStateEvent(sessionId, "cancelled"));
    }

    /// <summary>
    /// Approve or reject a human-in-the-loop gate (resumes a bookmark).
    /// </summary>
    public async Task Approve(string sessionId, bool decision)
    {
        _logger.LogInformation(
            "Approval for session {SessionId}: {Decision}", sessionId, decision);

        // Resume the workflow by dispatching to the instance with approval input
        var request = new DispatchWorkflowInstanceRequest(sessionId)
        {
            BookmarkId = null, // Will resume the first available bookmark
            Input = new Dictionary<string, object>
            {
                ["Decision"] = decision ? "approve" : "reject"
            }
        };

        await _dispatcher.DispatchAsync(request, CancellationToken.None);

        await Clients.Group(sessionId).SendAsync("approvalProcessed",
            new { SessionId = sessionId, Approved = decision });
    }

    /// <summary>
    /// Get buffered output for a session (for late-joining clients).
    /// </summary>
    public Task<string[]> GetSessionOutput(string sessionId)
    {
        return Task.FromResult(_tracker.GetOutput(sessionId));
    }

    /// <summary>
    /// Subscribe to a session's real-time events (add to SignalR group).
    /// </summary>
    public async Task JoinSession(string sessionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);

        var sessions = ConnectionSessions.GetOrAdd(Context.ConnectionId, _ => new HashSet<string>());
        lock (sessions) { sessions.Add(sessionId); }
    }

    /// <summary>
    /// Unsubscribe from a session's events.
    /// </summary>
    public async Task LeaveSession(string sessionId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionId);

        if (ConnectionSessions.TryGetValue(Context.ConnectionId, out var sessions))
        {
            lock (sessions) { sessions.Remove(sessionId); }
        }
    }

    /// <summary>
    /// Get info about a specific session.
    /// </summary>
    public Task<SessionInfo?> GetSession(string sessionId)
    {
        return Task.FromResult(_tracker.GetSession(sessionId));
    }

    /// <summary>
    /// List all active sessions.
    /// </summary>
    public Task<IReadOnlyCollection<SessionInfo>> ListSessions()
    {
        return Task.FromResult(_tracker.GetAllSessions());
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (ConnectionSessions.TryRemove(Context.ConnectionId, out var sessions))
        {
            foreach (var sessionId in sessions)
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionId);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    private static Dictionary<string, object> BuildWorkflowInput(
        string prompt,
        string workspacePath,
        string resolvedAssistant,
        string model,
        int modelPower,
        string structuredOutputSchema) =>
        new()
        {
            ["Prompt"] = prompt,
            ["WorkspacePath"] = workspacePath,
            ["AiAssistant"] = resolvedAssistant,
            ["Agent"] = resolvedAssistant,
            ["Model"] = string.IsNullOrWhiteSpace(model) ? "auto" : model,
            ["ModelPower"] = modelPower,
            ["StructuredOutputSchema"] = structuredOutputSchema
        };

    private async Task<string> ResolveDefinitionVersionIdAsync(string definitionId, CancellationToken cancellationToken)
    {
        var definition = await _workflowDefinitionService.FindWorkflowDefinitionAsync(
            WorkflowDefinitionHandle.ByDefinitionId(definitionId, VersionOptions.Published),
            cancellationToken);
        return definition?.Id
            ?? throw new InvalidOperationException(
                $"No published workflow definition found for '{definitionId}'.");
    }

}

/// <summary>
/// Result returned when a session is created.
/// </summary>
public record CreateSessionResult(string SessionId, string WorkflowName);
