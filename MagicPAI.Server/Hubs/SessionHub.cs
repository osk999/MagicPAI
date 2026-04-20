// MagicPAI.Server/Hubs/SessionHub.cs
// Temporal-based SignalR hub per temporal.md §J.2. Session creation moved to
// the REST controller; the hub now focuses on group membership and signal
// dispatch for gate approvals and prompt injection.
using MagicPAI.Core.Models;
using MagicPAI.Server.Bridge;
using MagicPAI.Shared.Hubs;
using Microsoft.AspNetCore.SignalR;
using Temporalio.Client;

namespace MagicPAI.Server.Hubs;

/// <summary>
/// SignalR hub for live session telemetry. Clients:
///   • Join/Leave SignalR groups keyed by session id (workflow id).
///   • Send Temporal signals for gate approval, rejection, or prompt injection.
///   • Request cancellation / termination.
///   • Query the in-memory tracker for recent state.
/// </summary>
public class SessionHub : Hub<ISessionHubClient>
{
    private readonly ITemporalClient _temporal;
    private readonly SessionTracker _tracker;
    private readonly ILogger<SessionHub> _log;

    public SessionHub(
        ITemporalClient temporal,
        SessionTracker tracker,
        ILogger<SessionHub> log)
    {
        _temporal = temporal;
        _tracker = tracker;
        _log = log;
    }

    // ───────────────────────────────────────────────────────────────────────
    // Group membership
    // ───────────────────────────────────────────────────────────────────────

    public Task JoinSession(string sessionId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, sessionId);

    public Task LeaveSession(string sessionId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionId);

    // ───────────────────────────────────────────────────────────────────────
    // Temporal signals — each forwards to the running workflow by id.
    // ───────────────────────────────────────────────────────────────────────

    public async Task ApproveGate(string sessionId, string approver, string? comment = null)
    {
        try
        {
            var handle = _temporal.GetWorkflowHandle(sessionId);
            await handle.SignalAsync("ApproveGate", new object[] { approver, comment ?? "" });
            _log.LogInformation("Gate approved for {Id} by {Who}", sessionId, approver);
        }
        catch (Temporalio.Exceptions.RpcException ex)
            when (ex.Code == Temporalio.Exceptions.RpcException.StatusCode.NotFound)
        {
            _log.LogWarning("ApproveGate on unknown session {Id}", sessionId);
        }
    }

    public async Task RejectGate(string sessionId, string reason)
    {
        try
        {
            var handle = _temporal.GetWorkflowHandle(sessionId);
            await handle.SignalAsync("RejectGate", new object[] { reason });
            _log.LogInformation("Gate rejected for {Id}: {Reason}", sessionId, reason);
        }
        catch (Temporalio.Exceptions.RpcException ex)
            when (ex.Code == Temporalio.Exceptions.RpcException.StatusCode.NotFound)
        {
            _log.LogWarning("RejectGate on unknown session {Id}", sessionId);
        }
    }

    public async Task InjectPrompt(string sessionId, string newPrompt)
    {
        try
        {
            var handle = _temporal.GetWorkflowHandle(sessionId);
            await handle.SignalAsync("InjectPrompt", new object[] { newPrompt });
        }
        catch (Temporalio.Exceptions.RpcException ex)
            when (ex.Code == Temporalio.Exceptions.RpcException.StatusCode.NotFound)
        {
            _log.LogWarning("InjectPrompt on unknown session {Id}", sessionId);
        }
    }

    /// <summary>Request graceful cancellation of a running workflow.</summary>
    public async Task CancelSession(string sessionId)
    {
        try
        {
            var handle = _temporal.GetWorkflowHandle(sessionId);
            await handle.CancelAsync();
            _tracker.UpdateState(sessionId, "cancelled");
            _log.LogInformation("Cancel requested for {Id}", sessionId);
        }
        catch (Temporalio.Exceptions.RpcException ex)
            when (ex.Code == Temporalio.Exceptions.RpcException.StatusCode.NotFound)
        {
            _log.LogWarning("Cancel on unknown session {Id}", sessionId);
        }
    }

    /// <summary>Force-terminate a workflow (skips cleanup hooks).</summary>
    public async Task TerminateSession(string sessionId, string? reason = null)
    {
        try
        {
            var handle = _temporal.GetWorkflowHandle(sessionId);
            await handle.TerminateAsync(reason ?? "Terminated from hub");
            _tracker.UpdateState(sessionId, "terminated");
            _log.LogWarning("Terminate requested for {Id}: {Reason}", sessionId, reason ?? "(none)");
        }
        catch (Temporalio.Exceptions.RpcException ex)
            when (ex.Code == Temporalio.Exceptions.RpcException.StatusCode.NotFound)
        {
            _log.LogWarning("Terminate on unknown session {Id}", sessionId);
        }
    }

    // ───────────────────────────────────────────────────────────────────────
    // Client-side queries against the in-memory tracker (for late-joiners).
    // ───────────────────────────────────────────────────────────────────────

    public Task<string[]> GetSessionOutput(string sessionId) =>
        Task.FromResult(_tracker.GetOutput(sessionId));

    public Task<SessionInfo?> GetSession(string sessionId) =>
        Task.FromResult(_tracker.GetSession(sessionId));

    public Task<IReadOnlyCollection<SessionInfo>> ListSessions() =>
        Task.FromResult(_tracker.GetAllSessions());
}
