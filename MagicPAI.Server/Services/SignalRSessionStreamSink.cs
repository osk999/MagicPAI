using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using MagicPAI.Core.Services;
using MagicPAI.Server.Hubs;
using MagicPAI.Shared.Hubs;

namespace MagicPAI.Server.Services;

/// <summary>
/// Temporal-side <see cref="ISessionStreamSink"/> implementation that routes
/// session-scoped events into the existing SignalR <see cref="SessionHub"/>
/// group for that session id. Streams bypass workflow history per temporal.md §11.5.
///
/// Phase 1 note: The existing <see cref="SessionHub"/> uses a plain
/// <see cref="Hub"/> (no strongly-typed client), so we use
/// <see cref="IHubContext{THub}"/> + <c>SendAsync(methodName, args)</c>. The
/// method names are kept in lock-step with <see cref="ISessionHubClient"/> so
/// Phase 2 can migrate to <c>Hub&lt;ISessionHubClient&gt;</c> with no protocol
/// change on the wire.
/// </summary>
public class SignalRSessionStreamSink : ISessionStreamSink
{
    private readonly IHubContext<SessionHub> _hub;
    private readonly ILogger<SignalRSessionStreamSink> _log;

    public SignalRSessionStreamSink(
        IHubContext<SessionHub> hub,
        ILogger<SignalRSessionStreamSink> log)
    {
        _hub = hub;
        _log = log;
    }

    public Task EmitChunkAsync(string sessionId, string line, CancellationToken ct) =>
        _hub.Clients.Group(sessionId).SendAsync(nameof(ISessionHubClient.OutputChunk), line, ct);

    public Task EmitStructuredAsync(string sessionId, string eventName, object payload, CancellationToken ct) =>
        _hub.Clients.Group(sessionId).SendAsync(nameof(ISessionHubClient.StructuredEvent), eventName, payload, ct);

    public Task EmitStageAsync(string sessionId, string stage, CancellationToken ct) =>
        _hub.Clients.Group(sessionId).SendAsync(nameof(ISessionHubClient.StageChanged), stage, ct);

    public Task EmitCostAsync(string sessionId, decimal totalCostUsd, CancellationToken ct = default)
    {
        // Studio's CostDisplay reads CostEntry.TotalUsd. Increment / per-call fields
        // are not tracked by the workflow today (only the running total accumulates
        // via ClaudeRunner.ExtractCost), so they are surfaced as zero. The mid-run
        // tile will reflect the correct cumulative spend after each LLM call.
        var entry = new CostEntry(
            SessionId: sessionId,
            IncrementUsd: 0m,
            TotalUsd: totalCostUsd,
            Agent: "",
            Model: "",
            InputTokens: 0,
            OutputTokens: 0);
        return _hub.Clients.Group(sessionId).SendAsync(
            nameof(ISessionHubClient.CostUpdate), entry, ct);
    }

    public Task CompleteSessionAsync(string sessionId, CancellationToken ct)
    {
        var payload = new SessionCompletedPayload(
            SessionId: sessionId,
            WorkflowType: "",
            CompletedAt: DateTime.UtcNow,
            TotalCostUsd: 0,
            Result: null);
        return _hub.Clients.Group(sessionId).SendAsync(
            nameof(ISessionHubClient.SessionCompleted), payload, ct);
    }
}
