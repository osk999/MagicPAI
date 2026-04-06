using Elsa.Mediator.Contracts;
using Elsa.Workflows;
using Elsa.Workflows.Notifications;
using MagicPAI.Core.Models;
using MagicPAI.Server.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace MagicPAI.Server.Bridge;

/// <summary>
/// Listens for WorkflowExecuted notifications to update session state
/// when a workflow completes, faults, or is cancelled.
/// </summary>
public class WorkflowCompletionHandler : INotificationHandler<WorkflowExecuted>
{
    private readonly IHubContext<SessionHub> _hubContext;
    private readonly SessionTracker _tracker;
    private readonly ILogger<WorkflowCompletionHandler> _logger;

    public WorkflowCompletionHandler(
        IHubContext<SessionHub> hubContext,
        SessionTracker tracker,
        ILogger<WorkflowCompletionHandler> logger)
    {
        _hubContext = hubContext;
        _tracker = tracker;
        _logger = logger;
    }

    public async Task HandleAsync(WorkflowExecuted notification, CancellationToken ct)
    {
        var workflowState = notification.WorkflowState;
        var instanceId = workflowState.Id;

        var session = _tracker.GetSession(instanceId);
        if (session is null)
            return;

        var status = workflowState.Status;
        var subStatus = workflowState.SubStatus;

        string newState = status switch
        {
            WorkflowStatus.Finished when subStatus == WorkflowSubStatus.Finished => "completed",
            WorkflowStatus.Finished when subStatus == WorkflowSubStatus.Faulted => "failed",
            WorkflowStatus.Finished when subStatus == WorkflowSubStatus.Cancelled => "cancelled",
            WorkflowStatus.Running => "running",
            _ => "unknown"
        };

        if (newState is "completed" or "failed" or "cancelled")
        {
            _tracker.UpdateState(instanceId, newState);

            await _hubContext.Clients.Group(instanceId).SendAsync(
                "sessionStateChanged",
                new SessionStateEvent(instanceId, newState),
                ct);

            _logger.LogInformation(
                "Workflow {InstanceId} finished with state {State}/{SubStatus}",
                instanceId, status, subStatus);
        }
    }
}
