namespace MagicPAI.Shared.Hubs;

/// <summary>
/// Methods the server calls on connected clients. Implemented by the Blazor-side
/// SessionHubClient wrapper via SignalR's .On&lt;T&gt;(...) registration.
/// See temporal.md §J.1.
/// </summary>
public interface ISessionHubClient
{
    Task OutputChunk(string line);
    Task StructuredEvent(string eventName, object payload);
    Task StageChanged(string stage);
    Task CostUpdate(CostEntry cost);
    Task VerificationResult(VerifyGateResult result);
    Task GateAwaiting(GateAwaitingPayload payload);
    Task ContainerSpawned(ContainerSpawnedPayload payload);
    Task ContainerDestroyed(ContainerDestroyedPayload payload);
    Task SessionCompleted(SessionCompletedPayload payload);
    Task SessionFailed(SessionFailedPayload payload);
    Task SessionCancelled(SessionCancelledPayload payload);
}
