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

        var workflowInstanceId = ResolveSessionId(notification.WorkflowExecutionContext);

        foreach (var record in notification.Records)
        {
            await HandleActivityStateRecord(workflowInstanceId, record, ct);

            foreach (var (eventName, message) in ExtractEvents(record))
            {
                switch (eventName)
                {
                    case "OutputChunk":
                    case "StreamChunk":
                        await HandleOutputChunk(workflowInstanceId, record.ActivityName, message, ct);
                        break;

                    case "VerificationComplete":
                        await HandleVerificationUpdate(workflowInstanceId, message, ct);
                        break;

                    case "CostUpdate":
                        await HandleCostUpdate(workflowInstanceId, message, ct);
                        break;

                    case "ContainerSpawned":
                    case "ContainerDestroyed":
                        await HandleContainerEvent(workflowInstanceId, eventName, message, ct);
                        break;

                    case "TriageResult":
                    case "WebsiteTaskResult":
                    case "ArchitectResult":
                    case "RepairPromptGenerated":
                    case "PromptTransform":
                        await HandleTaskEvent(workflowInstanceId, eventName, message, ct);
                        break;

                    default:
                        // Forward any *Failed events as errors to the UI
                        if (eventName.EndsWith("Failed", StringComparison.OrdinalIgnoreCase))
                        {
                            await HandleErrorEvent(workflowInstanceId, eventName, message, ct);
                        }
                        break;
                }
            }
        }
    }

    private async Task HandleActivityStateRecord(string sessionId, object record, CancellationToken ct)
    {
        var rawEventName = GetProperty(record, "EventName")?.ToString();
        var status = rawEventName switch
        {
            "Started" => "running",
            "Completed" => "completed",
            "Faulted" => "failed",
            _ => null
        };

        if (status is null)
            return;

        var activityName = GetProperty(record, "ActivityId")?.ToString();
        if (string.IsNullOrWhiteSpace(activityName))
            activityName = GetProperty(record, "ActivityName")?.ToString();
        if (string.IsNullOrWhiteSpace(activityName))
            activityName = GetProperty(record, "ActivityType")?.ToString();
        if (string.IsNullOrWhiteSpace(activityName))
            return;

        _tracker.UpdateActivity(sessionId, activityName, status);

        await _hubContext.Clients.Group(sessionId).SendAsync(
            "workflowProgress",
            new WorkflowProgressEvent(sessionId, activityName, status, 0, 0),
            ct);

        if (status == "failed")
        {
            _tracker.UpdateState(sessionId, "failed");
            await _hubContext.Clients.Group(sessionId).SendAsync(
                "sessionStateChanged",
                new SessionStateEvent(sessionId, "failed"),
                ct);
        }
    }

    private static IEnumerable<(string EventName, string Message)> ExtractEvents(object record)
    {
        var payload = GetProperty(record, "Payload");
        if (payload is IDictionary<string, object> dict)
        {
            foreach (var (eventName, value) in dict)
            {
                if (!string.IsNullOrWhiteSpace(eventName))
                    yield return (eventName, value?.ToString() ?? "");
            }

            yield break;
        }

        if (payload is IEnumerable<KeyValuePair<string, object>> pairs)
        {
            foreach (var (eventName, value) in pairs)
            {
                if (!string.IsNullOrWhiteSpace(eventName))
                    yield return (eventName, value?.ToString() ?? "");
            }

            yield break;
        }

        if (payload is JsonElement json && json.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in json.EnumerateObject())
                yield return (property.Name, property.Value.ToString());

            yield break;
        }

        var eventNameValue = GetProperty(record, "EventName")?.ToString();
        if (!string.IsNullOrWhiteSpace(eventNameValue))
            yield return (eventNameValue, GetProperty(record, "Message")?.ToString() ?? "");
    }

    private static object? GetProperty(object instance, string name) =>
        instance.GetType().GetProperty(name)?.GetValue(instance);

    private string ResolveSessionId(Elsa.Workflows.WorkflowExecutionContext workflowExecutionContext)
    {
        var parentWorkflowInstanceId = workflowExecutionContext.ParentWorkflowInstanceId;
        if (!string.IsNullOrWhiteSpace(parentWorkflowInstanceId) && _tracker.GetSession(parentWorkflowInstanceId) is not null)
            return parentWorkflowInstanceId;

        return workflowExecutionContext.Id;
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

    private async Task HandleCostUpdate(
        string sessionId, string message, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;
            var cost = root.TryGetProperty("costUsd", out var costProp) ? costProp.GetDecimal() : 0m;
            var inputTokens = root.TryGetProperty("inputTokens", out var inputProp) ? inputProp.GetInt32() : 0;
            var outputTokens = root.TryGetProperty("outputTokens", out var outputProp) ? outputProp.GetInt32() : 0;
            _tracker.UpdateCost(sessionId, cost);

            await _hubContext.Clients.Group(sessionId).SendAsync(
                "costUpdate",
                new CostUpdateEvent(sessionId, cost, inputTokens, outputTokens),
                ct);
        }
        catch (JsonException)
        {
            _logger.LogDebug("Could not parse CostUpdate payload for session {SessionId}: {Message}", sessionId, message);
        }
    }

    private async Task HandleContainerEvent(
        string sessionId, string eventName, string message, CancellationToken ct)
    {
        var containerId = ExtractContainerId(message);
        string? guiUrl = null;

        if (eventName == "ContainerDestroyed")
        {
            _tracker.UpdateContainer(sessionId, null);
        }
        else if (!string.IsNullOrWhiteSpace(containerId))
        {
            // Try to extract GuiUrl from the message or resolve from container manager
            guiUrl = ExtractGuiUrl(message);
            _tracker.UpdateContainer(sessionId, containerId, guiUrl);
        }

        await _hubContext.Clients.Group(sessionId).SendAsync(
            "containerEvent",
            new ContainerEvent(sessionId, containerId ?? message, guiUrl),
            ct);
    }

    private static string? ExtractGuiUrl(string message)
    {
        // Try JSON parse first: { "containerId": "...", "guiUrl": "..." }
        try
        {
            using var doc = JsonDocument.Parse(message);
            if (doc.RootElement.TryGetProperty("guiUrl", out var url))
                return url.GetString();
        }
        catch (JsonException) { }

        // Try to find URL pattern in the message
        var idx = message.IndexOf("http://", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) idx = message.IndexOf("https://", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            var end = message.IndexOfAny([' ', '\n', '\r', ')'], idx);
            return end > idx ? message[idx..end] : message[idx..];
        }

        return null;
    }

    private async Task HandleTaskEvent(
        string sessionId, string eventName, string message, CancellationToken ct)
    {
        var insight = BuildInsight(sessionId, eventName, message);
        if (insight is not null)
        {
            _tracker.AppendInsight(sessionId, insight);
            await _hubContext.Clients.Group(sessionId).SendAsync("taskInsight", insight, ct);
        }

        // Task events use workflowProgress channel
        await _hubContext.Clients.Group(sessionId).SendAsync(
            "workflowProgress",
            new WorkflowProgressEvent(sessionId, eventName, "completed", 0, 0),
            ct);
    }

    private async Task HandleErrorEvent(
        string sessionId, string eventName, string message, CancellationToken ct)
    {
        _logger.LogWarning("Activity error in session {SessionId}: {EventName}: {Message}",
            sessionId, eventName, message);

        // Also append error to the output buffer so late-joining clients see it
        var errorText = $"[{eventName}] {message}";
        _tracker.AppendOutput(sessionId, errorText);

        await _hubContext.Clients.Group(sessionId).SendAsync(
            "error",
            new ErrorEvent(sessionId, errorText),
            ct);
    }

    private static string? ExtractContainerId(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(message);
            if (doc.RootElement.TryGetProperty("containerId", out var containerId))
                return containerId.GetString();
        }
        catch (JsonException)
        {
        }

        var parts = message.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return null;

        if (!parts[0].Equals("Container", StringComparison.OrdinalIgnoreCase))
            return null;

        return parts[1];
    }

    private static TaskInsightEvent BuildInsight(string sessionId, string eventName, string message)
    {
        var timestamp = DateTime.UtcNow;

        try
        {
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;
            return eventName switch
            {
                "TriageResult" => new TaskInsightEvent(
                    sessionId,
                    Kind: "classifier",
                    Title: "Classifier Verdict",
                    Summary: $"Complexity {TryGetInt(root, "complexity", 5)}, category {TryGetString(root, "category", "code_gen")}",
                    Verdict: TryGetString(root, "outcome", null),
                    BeforeText: null,
                    AfterText: null,
                    RawPayload: message,
                    TimestampUtc: timestamp),
                "WebsiteTaskResult" => new TaskInsightEvent(
                    sessionId,
                    Kind: "classifier",
                    Title: "Website Routing Verdict",
                    Summary: TryGetString(root, "rationale", "Website routing evaluated") ?? "Website routing evaluated",
                    Verdict: TryGetString(root, "verdict", null),
                    BeforeText: null,
                    AfterText: null,
                    RawPayload: message,
                    TimestampUtc: timestamp),
                "ArchitectResult" => new TaskInsightEvent(
                    sessionId,
                    Kind: "architect",
                    Title: "Architect Decomposition",
                    Summary: $"Decomposed into {TryGetInt(root, "taskCount", 0)} sub-task(s)",
                    Verdict: null,
                    BeforeText: null,
                    AfterText: root.TryGetProperty("tasks", out var tasks) ? tasks.GetRawText() : null,
                    RawPayload: message,
                    TimestampUtc: timestamp),
                "RepairPromptGenerated" => new TaskInsightEvent(
                    sessionId,
                    Kind: "repair",
                    Title: "Repair Prompt",
                    Summary: $"Attempt {TryGetInt(root, "attempt", 0)}/{TryGetInt(root, "maxAttempts", 0)} for {TryGetString(root, "failedGatesSummary", "failed gates")}",
                    Verdict: "repair",
                    BeforeText: null,
                    AfterText: TryGetString(root, "repairPrompt", null),
                    RawPayload: message,
                    TimestampUtc: timestamp),
                "PromptTransform" => new TaskInsightEvent(
                    sessionId,
                    Kind: "prompt-transform",
                    Title: TryGetString(root, "label", "Prompt Transform") ?? "Prompt Transform",
                    Summary: TryGetString(root, "summary", "Prompt was transformed") ?? "Prompt was transformed",
                    Verdict: TryGetString(root, "verdict", null),
                    BeforeText: TryGetString(root, "before", null),
                    AfterText: TryGetString(root, "after", null),
                    RawPayload: message,
                    TimestampUtc: timestamp),
                _ => new TaskInsightEvent(
                    sessionId,
                    Kind: "workflow",
                    Title: eventName,
                    Summary: message,
                    Verdict: null,
                    BeforeText: null,
                    AfterText: null,
                    RawPayload: message,
                    TimestampUtc: timestamp)
            };
        }
        catch (JsonException)
        {
            return new TaskInsightEvent(
                sessionId,
                Kind: "workflow",
                Title: eventName,
                Summary: message,
                Verdict: null,
                BeforeText: null,
                AfterText: null,
                RawPayload: message,
                TimestampUtc: timestamp);
        }
    }

    private static int TryGetInt(JsonElement root, string propertyName, int fallback) =>
        root.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var result) ? result : fallback;

    private static string? TryGetString(JsonElement root, string propertyName, string? fallback) =>
        root.TryGetProperty(propertyName, out var value) ? value.GetString() ?? fallback : fallback;
}
