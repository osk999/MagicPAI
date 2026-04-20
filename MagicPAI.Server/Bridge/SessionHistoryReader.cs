// MagicPAI.Server/Bridge/SessionHistoryReader.cs
// Temporal-backed session history reader per temporal.md §12.6. Queries
// Temporal's visibility store (ListWorkflowsAsync) for recent workflow
// executions and hydrates per-session cost from the MagicPAI DB when
// available. During Phase 2 the DB schema may not yet have a cost table;
// the reader returns 0 in that case.
using System.Runtime.CompilerServices;
using Temporalio.Client;
using Temporalio.Common;

namespace MagicPAI.Server.Bridge;

public class SessionHistoryReader
{
    private readonly ITemporalClient _temporal;
    private readonly ILogger<SessionHistoryReader> _log;

    public SessionHistoryReader(ITemporalClient temporal, ILogger<SessionHistoryReader> log)
    {
        _temporal = temporal;
        _log = log;
    }

    /// <summary>
    /// Enumerates recent sessions (workflows started within <paramref name="window"/>)
    /// newest first, capped at <paramref name="take"/>.
    /// </summary>
    public async IAsyncEnumerable<SessionSummary> ListRecentAsync(
        TimeSpan window,
        int take = 100,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var since = DateTime.UtcNow - window;
        // NOTE: the default Temporal visibility store (SQL) does not support
        // `ORDER BY` in list queries — it throws "invalid query: operation is
        // not supported: 'order by' clause". We fetch the filtered set (up to
        // 2x `take` to give the sort some headroom for out-of-order arrivals)
        // and sort client-side, capping at `take`.
        var query = $"StartTime > \"{since:yyyy-MM-ddTHH:mm:ss.fffZ}\"";

        var fetched = new List<WorkflowExecution>();
        var fetchCap = Math.Max(take * 2, take);
        await foreach (var wf in _temporal
            .ListWorkflowsAsync(query,
                new WorkflowListOptions { Rpc = new RpcOptions { CancellationToken = ct } })
            .WithCancellation(ct))
        {
            fetched.Add(wf);
            if (fetched.Count >= fetchCap) break;
        }

        foreach (var wf in fetched
            .OrderByDescending(w => w.StartTime)
            .Take(take))
        {
            var assistant = TryGetAttribute(wf, "MagicPaiAiAssistant");

            yield return new SessionSummary(
                SessionId: wf.Id,
                WorkflowType: wf.WorkflowType,
                Status: wf.Status.ToString(),
                StartTime: wf.StartTime,
                CloseTime: wf.CloseTime,
                AiAssistant: assistant ?? "",
                TotalCostUsd: 0m);  // Hydrated from cost_tracking in Phase 3
        }
    }

    /// <summary>
    /// Enumerate the event history for one workflow — used for debugging and
    /// deterministic replay verification.
    /// </summary>
    public async IAsyncEnumerable<WorkflowHistoryEventSummary> GetEventsAsync(
        string sessionId,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var handle = _temporal.GetWorkflowHandle(sessionId);
        await foreach (var evt in handle
            .FetchHistoryEventsAsync(
                new WorkflowHistoryEventFetchOptions { Rpc = new RpcOptions { CancellationToken = ct } })
            .WithCancellation(ct))
        {
            yield return new WorkflowHistoryEventSummary(
                EventId: evt.EventId,
                EventType: evt.EventType.ToString(),
                EventTime: evt.EventTime?.ToDateTime() ?? DateTime.MinValue,
                Attributes: evt.AttributesCase.ToString());
        }
    }

    private static string? TryGetAttribute(WorkflowExecution wf, string key)
    {
        try
        {
            var k = SearchAttributeKey.CreateText(key);
            return wf.TypedSearchAttributes.TryGetValue(k, out var value) ? value : null;
        }
        catch
        {
            return null;
        }
    }
}

public record SessionSummary(
    string SessionId,
    string WorkflowType,
    string Status,
    DateTime StartTime,
    DateTime? CloseTime,
    string AiAssistant,
    decimal TotalCostUsd);

public record WorkflowHistoryEventSummary(
    long EventId,
    string EventType,
    DateTime EventTime,
    string Attributes);
