using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using MagicPAI.Core.Models;

namespace MagicPAI.Studio.Services;

/// <summary>
/// Type-safe SignalR client for the MagicPAI SessionHub.
/// Provides strongly typed events and hub method invocations.
/// </summary>
public class SessionHubClient : IAsyncDisposable
{
    private readonly HubConnection _connection;
    private bool _started;

    // Type-safe events
    public event Action<OutputChunkEvent>? OnOutputChunk;
    public event Action<WorkflowProgressEvent>? OnWorkflowProgress;
    public event Action<VerificationUpdateEvent>? OnVerificationUpdate;
    public event Action<CostUpdateEvent>? OnCostUpdate;
    public event Action<SessionStateEvent>? OnSessionStateChanged;
    public event Action<ContainerEvent>? OnContainerSpawned;
    public event Action<ErrorEvent>? OnError;

    public HubConnectionState State => _connection.State;

    public SessionHubClient(IConfiguration config)
    {
        var hubUrl = config["MagicPAI:HubUrl"] ?? "http://localhost:5000/hub";
        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect([
                TimeSpan.Zero,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(3),
                TimeSpan.FromSeconds(5)
            ])
            .Build();

        // Register event handlers with strong types
        _connection.On<OutputChunkEvent>("outputChunk",
            e => OnOutputChunk?.Invoke(e));
        _connection.On<WorkflowProgressEvent>("workflowProgress",
            e => OnWorkflowProgress?.Invoke(e));
        _connection.On<VerificationUpdateEvent>("verificationUpdate",
            e => OnVerificationUpdate?.Invoke(e));
        _connection.On<CostUpdateEvent>("costUpdate",
            e => OnCostUpdate?.Invoke(e));
        _connection.On<SessionStateEvent>("sessionStateChanged",
            e => OnSessionStateChanged?.Invoke(e));
        _connection.On<ContainerEvent>("containerEvent",
            e => OnContainerSpawned?.Invoke(e));
        _connection.On<ErrorEvent>("error",
            e => OnError?.Invoke(e));
    }

    /// <summary>Connect to the SignalR hub if not already connected.</summary>
    public async Task ConnectAsync()
    {
        if (_started) return;
        await _connection.StartAsync();
        _started = true;
    }

    /// <summary>Create a new session and start its workflow. Returns the session ID.</summary>
    public async Task<string> CreateSessionAsync(
        string prompt, string workspacePath,
        string agent = "claude", string model = "auto")
    {
        // Hub returns CreateSessionResult { SessionId, WorkflowName }
        // We extract just the SessionId for the client
        var result = await _connection.InvokeAsync<CreateSessionResult>(
            "CreateSession", prompt, workspacePath, agent, model);
        return result?.SessionId ?? "";
    }

    // Mirror of server's CreateSessionResult for deserialization
    private record CreateSessionResult(string SessionId, string WorkflowName);

    /// <summary>Stop a running session.</summary>
    public async Task StopSessionAsync(string sessionId)
    {
        await _connection.InvokeAsync("StopSession", sessionId);
    }

    /// <summary>Approve or reject a human approval gate.</summary>
    public async Task ApproveAsync(string sessionId, bool approve)
    {
        await _connection.InvokeAsync("Approve", sessionId, approve);
    }

    public async ValueTask DisposeAsync()
    {
        if (_started)
        {
            await _connection.DisposeAsync();
        }
    }
}
