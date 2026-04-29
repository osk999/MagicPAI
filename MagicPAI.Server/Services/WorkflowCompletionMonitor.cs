// MagicPAI.Server/Services/WorkflowCompletionMonitor.cs
// Bridges Temporal completion events to SignalR per temporal.md §J.5. Polls
// tracked sessions every few seconds; on close, emits SessionCompleted /
// SessionFailed / SessionCancelled and removes the tracker entry.
using MagicPAI.Server.Bridge;
using MagicPAI.Server.Hubs;
using MagicPAI.Shared.Hubs;
using Microsoft.AspNetCore.SignalR;
using Temporalio.Api.Enums.V1;
using Temporalio.Client;

namespace MagicPAI.Server.Services;

public class WorkflowCompletionMonitor : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly ITemporalClient _temporal;
    private readonly SessionTracker _tracker;
    private readonly IHubContext<SessionHub, ISessionHubClient> _hub;
    private readonly ILogger<WorkflowCompletionMonitor> _log;

    public WorkflowCompletionMonitor(
        ITemporalClient temporal,
        SessionTracker tracker,
        IHubContext<SessionHub, ISessionHubClient> hub,
        ILogger<WorkflowCompletionMonitor> log)
    {
        _temporal = temporal;
        _tracker = tracker;
        _hub = hub;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("WorkflowCompletionMonitor started (poll={Interval}s)", PollInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PollInterval, stoppingToken);
                await PollAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "WorkflowCompletionMonitor tick failed");
            }
        }

        _log.LogInformation("WorkflowCompletionMonitor stopped");
    }

    private async Task PollAsync(CancellationToken ct)
    {
        var active = _tracker.GetAllSessions()
            .Where(s => s.State is "running" or "idle")
            .Select(s => (s.Id, s.WorkflowId ?? "unknown"))
            .ToList();

        foreach (var (sessionId, workflowType) in active)
        {
            try
            {
                var desc = await _temporal.GetWorkflowHandle(sessionId).DescribeAsync();
                if (desc.CloseTime is null)
                    continue;

                switch (desc.Status)
                {
                    case WorkflowExecutionStatus.Completed:
                        var totalCostUsd = await TryQueryTotalCostAsync(sessionId);
                        await EmitCompletedAsync(sessionId, workflowType, desc.CloseTime.Value, totalCostUsd);
                        _tracker.UpdateState(sessionId, "completed");
                        break;

                    case WorkflowExecutionStatus.Failed:
                    case WorkflowExecutionStatus.TimedOut:
                    case WorkflowExecutionStatus.Terminated:
                        await EmitFailedAsync(sessionId, desc.Status.ToString());
                        _tracker.UpdateState(sessionId,
                            desc.Status == WorkflowExecutionStatus.Terminated ? "terminated" : "failed");
                        break;

                    case WorkflowExecutionStatus.Canceled:
                        await EmitCancelledAsync(sessionId);
                        _tracker.UpdateState(sessionId, "cancelled");
                        break;
                }
            }
            catch (Temporalio.Exceptions.RpcException ex)
                when (ex.Code == Temporalio.Exceptions.RpcException.StatusCode.NotFound)
            {
                _log.LogDebug("Session {Id} not found in Temporal; removing from tracker", sessionId);
                _tracker.RemoveSession(sessionId);
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Poll skipped for {Id}", sessionId);
            }
        }
    }

    private Task EmitCompletedAsync(string sessionId, string workflowType, DateTime closeTime, decimal totalCostUsd) =>
        _hub.Clients.Group(sessionId).SessionCompleted(new SessionCompletedPayload(
            SessionId: sessionId,
            WorkflowType: workflowType,
            CompletedAt: closeTime,
            TotalCostUsd: totalCostUsd,
            Result: null));

    /// <summary>
    /// Best-effort query for the running total cost from a closed workflow. Most
    /// orchestration workflows (FullOrchestrate, StandardOrchestrate, SimpleAgent,
    /// SmartImprove, IterativeLoop, SmartIterativeLoop) expose a
    /// <c>TotalCostUsd</c> <c>[WorkflowQuery]</c>. Workflows that do not (e.g.
    /// PromptEnhancer, OrchestrateComplexPath, OrchestrateSimplePath, WebsiteAudit*)
    /// will fail the query — log at debug and return 0.
    /// </summary>
    private async Task<decimal> TryQueryTotalCostAsync(string sessionId)
    {
        try
        {
            return await _temporal.GetWorkflowHandle(sessionId)
                .QueryAsync<decimal>("TotalCostUsd", Array.Empty<object>());
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex,
                "TotalCostUsd query unavailable for {SessionId}; defaulting cost to $0",
                sessionId);
            return 0m;
        }
    }

    private Task EmitFailedAsync(string sessionId, string reason) =>
        _hub.Clients.Group(sessionId).SessionFailed(new SessionFailedPayload(
            SessionId: sessionId,
            ErrorMessage: $"Workflow closed with status {reason}",
            ErrorType: reason));

    private Task EmitCancelledAsync(string sessionId) =>
        _hub.Clients.Group(sessionId).SessionCancelled(new SessionCancelledPayload(
            SessionId: sessionId,
            Reason: "Cancelled"));
}
