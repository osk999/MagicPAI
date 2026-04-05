using System.Text.Json;
using Elsa.Mediator.Contracts;
using Elsa.Workflows.Runtime.Notifications;
using MagicPAI.Core.Models;
using MagicPAI.Server.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace MagicPAI.Server.Bridge;

/// <summary>
/// Listens for Elsa activity execution log updates and forwards relevant events
/// to connected SignalR clients. Activities emit structured log entries via
/// context.AddExecutionLogEntry("EventName", payload) which are captured here.
/// </summary>
public class ElsaEventBridge : INotificationHandler<ActivityExecutionLogUpdated>
{
    private readonly IHubContext<SessionHub> _hubContext;
    private readonly SessionTracker _tracker;
    private readonly ILogger<ElsaEventBridge> _logger;

    public ElsaEventBridge(
        IHubContext<SessionHub> hubContext,
        SessionTracker tracker,
        ILogger<ElsaEventBridge> logger)
    {
        _hubContext = hubContext;
        _tracker = tracker;
        _logger = logger;
    }

    public async Task HandleAsync(ActivityExecutionLogUpdated notification, CancellationToken ct)
    {
        if (notification.WorkflowExecutionContext is null)
            return;

        var workflowInstanceId = notification.WorkflowExecutionContext.Id;

        foreach (var record in notification.Records)
        {
            // The Payload dictionary contains log entries added via AddExecutionLogEntry
            if (record.Payload is not IDictionary<string, object> payload)
                continue;

            foreach (var (eventName, value) in payload)
            {
                var message = value?.ToString() ?? "";

                switch (eventName)
                {
                    case "OutputChunk":
                        await HandleOutputChunk(workflowInstanceId, record.ActivityName, message, ct);
                        break;

                    case "VerificationComplete":
                        await HandleVerificationUpdate(workflowInstanceId, message, ct);
                        break;

                    case "ContainerSpawned":
                    case "ContainerDestroyed":
                        await HandleContainerEvent(workflowInstanceId, eventName, message, ct);
                        break;

                    case "TriageResult":
                    case "ArchitectResult":
                    case "RepairPromptGenerated":
                        await HandleTaskEvent(workflowInstanceId, eventName, message, ct);
                        break;
                }
            }
        }
    }

    private async Task HandleOutputChunk(
        string sessionId, string? activityName, string message, CancellationToken ct)
    {
        try
        {
            // OutputChunk payload is JSON: { "activityId": "...", "text": "..." }
            using var doc = JsonDocument.Parse(message);
            var text = doc.RootElement.TryGetProperty("text", out var t) ? t.GetString() ?? "" : message;

            _tracker.AppendOutput(sessionId, text);

            await _hubContext.Clients.Group(sessionId).SendAsync(
                "outputChunk",
                new OutputChunkEvent(sessionId, text, activityName),
                ct);
        }
        catch (JsonException)
        {
            // If not valid JSON, treat the whole message as text
            _tracker.AppendOutput(sessionId, message);

            await _hubContext.Clients.Group(sessionId).SendAsync(
                "outputChunk",
                new OutputChunkEvent(sessionId, message, activityName),
                ct);
        }
    }

    private async Task HandleVerificationUpdate(
        string sessionId, string message, CancellationToken ct)
    {
        // Send typed event matching VerificationUpdateEvent record
        await _hubContext.Clients.Group(sessionId).SendAsync(
            "verificationUpdate",
            new VerificationUpdateEvent(sessionId, "verification", true, message, []),
            ct);
    }

    private async Task HandleContainerEvent(
        string sessionId, string eventName, string message, CancellationToken ct)
    {
        // Send typed event matching ContainerEvent record
        await _hubContext.Clients.Group(sessionId).SendAsync(
            "containerEvent",
            new ContainerEvent(sessionId, message, null),
            ct);
    }

    private async Task HandleTaskEvent(
        string sessionId, string eventName, string message, CancellationToken ct)
    {
        // Task events use workflowProgress channel
        await _hubContext.Clients.Group(sessionId).SendAsync(
            "workflowProgress",
            new WorkflowProgressEvent(sessionId, eventName, "completed", 0, 0),
            ct);
    }
}
