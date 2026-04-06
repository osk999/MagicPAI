using System.Collections.Concurrent;
using Elsa.Mediator.Contracts;
using Elsa.Workflows.Runtime.Notifications;
using MagicPAI.Core.Models;
using MagicPAI.Server.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace MagicPAI.Server.Bridge;

/// <summary>
/// Tracks workflow execution progress by listening to ActivityExecutionRecordUpdated notifications.
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

        // Determine if activity completed or started
        var status = record.CompletedAt.HasValue ? "completed" : "running";

        // Track completed activity count per session
        var completed = 0;
        if (record.CompletedAt.HasValue)
            completed = _completedCounts.AddOrUpdate(sessionId, 1, (_, count) => count + 1);
        else
            _completedCounts.TryGetValue(sessionId, out completed);

        // Track total distinct activities seen (approximates total steps)
        var activityKey = $"{sessionId}:{record.ActivityId}";
        _seenActivities.TryAdd(activityKey, 0);
        var totalSeen = _seenActivities.Keys.Count(k => k.StartsWith($"{sessionId}:"));

        // Send progress update
        await _hubContext.Clients.Group(sessionId).SendAsync(
            "workflowProgress",
            new WorkflowProgressEvent(
                SessionId: sessionId,
                ActivityName: record.ActivityName ?? record.ActivityType,
                Status: status,
                CompletedSteps: completed,
                TotalSteps: totalSeen),
            ct);

        // Update session state based on activity status
        if (record.Status == Elsa.Workflows.ActivityStatus.Faulted)
        {
            _tracker.UpdateState(sessionId, "failed");

            await _hubContext.Clients.Group(sessionId).SendAsync(
                "sessionStateChanged",
                new SessionStateEvent(sessionId, "failed"),
                ct);
        }

        _logger.LogDebug(
            "Activity {ActivityType} ({ActivityName}) {Status} for session {SessionId} [{Completed}/{Total}]",
            record.ActivityType,
            record.ActivityName,
            status,
            sessionId,
            completed,
            totalSeen);
    }
}
