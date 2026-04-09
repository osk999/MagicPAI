using System.Collections.Concurrent;
using Elsa.Mediator.Contracts;
using Elsa.Workflows.Runtime.Notifications;
using MagicPAI.Core.Models;
using MagicPAI.Server.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace MagicPAI.Server.Bridge;

/// <summary>
/// Tracks workflow execution progress by listening to activity execution record updates.
/// Sends workflowProgress and sessionStateChanged events to SignalR clients.
/// </summary>
public class WorkflowProgressTracker : INotificationHandler<ActivityExecutionRecordUpdated>
{
    private readonly IHubContext<SessionHub> _hubContext;
    private readonly SessionTracker _tracker;
    private readonly ILogger<WorkflowProgressTracker> _logger;
    private readonly ConcurrentDictionary<string, int> _completedCounts = new();
    private readonly ConcurrentDictionary<string, int> _seenActivities = new();

    public WorkflowProgressTracker(
        IHubContext<SessionHub> hubContext,
        SessionTracker tracker,
        ILogger<WorkflowProgressTracker> logger)
    {
        _hubContext = hubContext;
        _tracker = tracker;
        _logger = logger;
    }

    public async Task HandleAsync(ActivityExecutionRecordUpdated notification, CancellationToken ct)
    {
        var record = notification.Record;
        var sessionId = record.WorkflowInstanceId;
        var session = _tracker.GetSession(sessionId);
        if (session is null)
            return;

        var status = record.Status switch
        {
            Elsa.Workflows.ActivityStatus.Running => "running",
            Elsa.Workflows.ActivityStatus.Faulted => "failed",
            Elsa.Workflows.ActivityStatus.Completed => "completed",
            _ => null
        };

        if (status is null)
            return;

        var activityName = !string.IsNullOrWhiteSpace(record.ActivityId)
            ? record.ActivityId
            : !string.IsNullOrWhiteSpace(record.ActivityName)
                ? record.ActivityName
                : record.ActivityType;

        var completed = 0;
        if (status == "completed")
            completed = _completedCounts.AddOrUpdate(sessionId, 1, (_, count) => count + 1);
        else
            _completedCounts.TryGetValue(sessionId, out completed);

        var activityKey = $"{sessionId}:{record.ActivityId}";
        _seenActivities.TryAdd(activityKey, 0);
        var totalSeen = _seenActivities.Keys.Count(k => k.StartsWith($"{sessionId}:"));

        _tracker.UpdateActivity(sessionId, activityName, status);

        await _hubContext.Clients.Group(sessionId).SendAsync(
            "workflowProgress",
            new WorkflowProgressEvent(
                SessionId: sessionId,
                ActivityName: activityName,
                Status: status,
                CompletedSteps: completed,
                TotalSteps: totalSeen),
            ct);

        if (status == "failed")
        {
            _tracker.UpdateState(sessionId, "failed");

            await _hubContext.Clients.Group(sessionId).SendAsync(
                "sessionStateChanged",
                new SessionStateEvent(sessionId, "failed"),
                ct);
        }

        _logger.LogDebug(
            "Activity status {ActivityStatus} for {ActivityType} ({ActivityName}) in session {SessionId} [{Completed}/{Total}]",
            record.Status,
            record.ActivityType,
            activityName,
            sessionId,
            completed,
            totalSeen);
    }
}
