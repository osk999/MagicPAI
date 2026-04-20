// MagicPAI.Studio/Services/SessionHubClient.cs
// Temporal migration §J.4: thin SignalR wrapper around /hub.
// Subscribes to every event defined in MagicPAI.Shared.Hubs.ISessionHubClient.
using MagicPAI.Shared.Hubs;
using Microsoft.AspNetCore.SignalR.Client;

namespace MagicPAI.Studio.Services;

/// <summary>
/// Browser-side SignalR client for the server-side SessionHub. Subscribes to
/// every method on <see cref="ISessionHubClient"/> and surfaces them as
/// strongly-typed C# events. Invokes hub methods (JoinSession, ApproveGate,
/// etc.) via <see cref="HubConnection.InvokeAsync(string, object?[])"/>.
/// </summary>
public class SessionHubClient : IAsyncDisposable
{
    private readonly HubConnection _conn;
    private bool _started;

    public event Action<string>? OutputChunk;
    public event Action<string, object>? StructuredEvent;
    public event Action<string>? StageChanged;
    public event Action<CostEntry>? CostUpdate;
    public event Action<VerifyGateResult>? VerificationResult;
    public event Action<GateAwaitingPayload>? GateAwaiting;
    public event Action<ContainerSpawnedPayload>? ContainerSpawned;
    public event Action<ContainerDestroyedPayload>? ContainerDestroyed;
    public event Action<SessionCompletedPayload>? SessionCompleted;
    public event Action<SessionFailedPayload>? SessionFailed;
    public event Action<SessionCancelledPayload>? SessionCancelled;

    public HubConnectionState State => _conn.State;

    public SessionHubClient(HttpClient http)
    {
        var hubUrl = new Uri(http.BaseAddress!, "hub");
        _conn = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect(new[]
            {
                TimeSpan.Zero,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(3),
                TimeSpan.FromSeconds(5)
            })
            .Build();

        _conn.On<string>("OutputChunk",
            line => OutputChunk?.Invoke(line));
        _conn.On<string, object>("StructuredEvent",
            (name, payload) => StructuredEvent?.Invoke(name, payload));
        _conn.On<string>("StageChanged",
            stage => StageChanged?.Invoke(stage));
        _conn.On<CostEntry>("CostUpdate",
            c => CostUpdate?.Invoke(c));
        _conn.On<VerifyGateResult>("VerificationResult",
            r => VerificationResult?.Invoke(r));
        _conn.On<GateAwaitingPayload>("GateAwaiting",
            p => GateAwaiting?.Invoke(p));
        _conn.On<ContainerSpawnedPayload>("ContainerSpawned",
            p => ContainerSpawned?.Invoke(p));
        _conn.On<ContainerDestroyedPayload>("ContainerDestroyed",
            p => ContainerDestroyed?.Invoke(p));
        _conn.On<SessionCompletedPayload>("SessionCompleted",
            p => SessionCompleted?.Invoke(p));
        _conn.On<SessionFailedPayload>("SessionFailed",
            p => SessionFailed?.Invoke(p));
        _conn.On<SessionCancelledPayload>("SessionCancelled",
            p => SessionCancelled?.Invoke(p));
    }

    public async Task StartAsync()
    {
        if (_started) return;
        await _conn.StartAsync();
        _started = true;
    }

    public async Task JoinSessionAsync(string sessionId)
    {
        await StartAsync();
        await _conn.InvokeAsync("JoinSession", sessionId);
    }

    public async Task LeaveSessionAsync(string sessionId)
    {
        if (!_started) return;
        try
        {
            await _conn.InvokeAsync("LeaveSession", sessionId);
        }
        catch
        {
            // Hub may already be disconnected — ignore.
        }
    }

    public Task ApproveGateAsync(string sessionId, string approver = "web-user", string? comment = null)
        => _conn.InvokeAsync("ApproveGate", sessionId, approver, comment);

    public Task RejectGateAsync(string sessionId, string reason)
        => _conn.InvokeAsync("RejectGate", sessionId, reason);

    public Task InjectPromptAsync(string sessionId, string newPrompt)
        => _conn.InvokeAsync("InjectPrompt", sessionId, newPrompt);

    public Task CancelSessionAsync(string sessionId)
        => _conn.InvokeAsync("CancelSession", sessionId);

    public async ValueTask DisposeAsync()
    {
        if (_started)
            await _conn.DisposeAsync();
    }
}
