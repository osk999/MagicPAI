using Elsa.Mediator.Contracts;
using Elsa.Workflows;
using Elsa.Workflows.Notifications;
using MagicPAI.Core.Models;
using MagicPAI.Core.Services;
using MagicPAI.Server.Hubs;
using MagicPAI.Server.Services;
using Microsoft.AspNetCore.SignalR;
using System.Text.Json;
using Npgsql;

namespace MagicPAI.Server.Bridge;

/// <summary>
/// Listens for WorkflowExecuted notifications to update session state
/// when a workflow completes, faults, or is cancelled.
/// </summary>
public class WorkflowCompletionHandler : INotificationHandler<WorkflowExecuted>
{
    private readonly IHubContext<SessionHub> _hubContext;
    private readonly SessionTracker _tracker;
    private readonly IContainerManager _containerManager;
    private readonly IGuiPortAllocator? _guiPortAllocator;
    private readonly SessionContainerLogStreamer _logStreamer;
    private readonly string? _pgConn;
    private readonly ILogger<WorkflowCompletionHandler> _logger;

    public WorkflowCompletionHandler(
        IHubContext<SessionHub> hubContext,
        SessionTracker tracker,
        IContainerManager containerManager,
        IGuiPortAllocator? guiPortAllocator,
        SessionContainerLogStreamer logStreamer,
        IConfiguration configuration,
        ILogger<WorkflowCompletionHandler> logger)
    {
        _hubContext = hubContext;
        _tracker = tracker;
        _containerManager = containerManager;
        _guiPortAllocator = guiPortAllocator;
        _logStreamer = logStreamer;
        _pgConn = configuration.GetConnectionString("MagicPai");
        _logger = logger;
    }

    public async Task HandleAsync(WorkflowExecuted notification, CancellationToken ct)
    {
        var workflowState = notification.WorkflowState;
        if (!string.IsNullOrWhiteSpace(workflowState.ParentWorkflowInstanceId))
            return;

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
            await _logStreamer.StopStreamingAsync(instanceId);
            await CleanupLeakedContainerAsync(instanceId, ct);
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

    private async Task CleanupLeakedContainerAsync(string instanceId, CancellationToken ct)
    {
        var containerId = _tracker.GetSession(instanceId)?.ContainerId;
        if (string.IsNullOrWhiteSpace(containerId))
        {
            var latestContainerEvent = await GetLatestContainerEventAsync(instanceId, ct);
            if (latestContainerEvent is null)
                return;

            if (!string.Equals(latestContainerEvent.EventName, "ContainerSpawned", StringComparison.Ordinal))
                return;

            containerId = ExtractContainerId(latestContainerEvent.Message);
        }

        if (string.IsNullOrWhiteSpace(containerId))
            return;

        try
        {
            await _containerManager.DestroyAsync(containerId, ct);
            _guiPortAllocator?.Release(instanceId);
            _tracker.UpdateContainer(instanceId, null);
            _logger.LogWarning(
                "Workflow {InstanceId} finished without container cleanup; destroyed leaked container {ContainerId}",
                instanceId,
                containerId);
        }
        catch (Exception ex)
        {
            _guiPortAllocator?.Release(instanceId);
            _logger.LogWarning(
                ex,
                "Workflow {InstanceId} finished with leaked container {ContainerId}, but cleanup failed",
                instanceId,
                containerId);
        }
    }

    private async Task<ContainerEventRecord?> GetLatestContainerEventAsync(string instanceId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_pgConn))
            return null;

        try
        {
            await using var conn = new NpgsqlConnection(_pgConn);
            await conn.OpenAsync(ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT "EventName", "Message"
                FROM "Elsa"."WorkflowExecutionLogRecords"
                WHERE "WorkflowInstanceId" = @id
                  AND "EventName" IN ('ContainerSpawned', 'ContainerDestroyed', 'ContainerDestroySkipped')
                ORDER BY "Timestamp" DESC, "Sequence" DESC
                LIMIT 1;
                """;
            cmd.Parameters.AddWithValue("id", instanceId);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                return null;

            return new ContainerEventRecord(
                reader.IsDBNull(0) ? "" : reader.GetString(0),
                reader.IsDBNull(1) ? "" : reader.GetString(1));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not inspect container cleanup history for workflow {InstanceId}", instanceId);
            return null;
        }
    }

    private static string? ExtractContainerId(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return null;

        try
        {
            using var json = JsonDocument.Parse(message);
            if (json.RootElement.TryGetProperty("containerId", out var containerIdElement))
                return containerIdElement.GetString();
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

    private sealed record ContainerEventRecord(string EventName, string Message);
}
