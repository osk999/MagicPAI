namespace MagicPAI.Core.Services;

/// <summary>
/// Side-channel sink for session-scoped event streaming. Temporal activities use this
/// to push live updates (CLI output, structured events, stage transitions) to connected
/// SignalR clients without routing data through workflow history.
///
/// Implementations are typically SignalR hub context wrappers on the server, and no-op
/// or in-memory collectors in tests. See temporal.md §11.5.
/// </summary>
public interface ISessionStreamSink
{
    /// <summary>Emit a raw CLI/log line chunk for a running session.</summary>
    Task EmitChunkAsync(string sessionId, string line, CancellationToken ct);

    /// <summary>Emit a structured event (e.g., ContainerSpawned, CostUpdate) with a name and JSON-serializable payload.</summary>
    Task EmitStructuredAsync(string sessionId, string eventName, object payload, CancellationToken ct);

    /// <summary>Emit a pipeline-stage transition for UI badges / status displays.</summary>
    Task EmitStageAsync(string sessionId, string stage, CancellationToken ct);

    /// <summary>Signal that a session has completed (success or failure). Final payload in the stream.</summary>
    Task CompleteSessionAsync(string sessionId, CancellationToken ct);
}
