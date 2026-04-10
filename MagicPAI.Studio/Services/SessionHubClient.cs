using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using MagicPAI.Shared.Models;

namespace MagicPAI.Studio.Services;

/// <summary>
/// Type-safe SignalR client for the MagicPAI SessionHub.
/// Provides strongly typed events and hub method invocations.
/// </summary>
public class SessionHubClient : IAsyncDisposable
{
    private readonly HubConnection _connection;
    private bool _started;

    public event Action<OutputChunkEvent>? OnOutputChunk;
    public event Action<WorkflowProgressEvent>? OnWorkflowProgress;
    public event Action<VerificationUpdateEvent>? OnVerificationUpdate;
    public event Action<CostUpdateEvent>? OnCostUpdate;
    public event Action<SessionStateEvent>? OnSessionStateChanged;
    public event Action<ContainerEvent>? OnContainerSpawned;
    public event Action<ContainerLogEvent>? OnContainerLog;
    public event Action<TaskInsightEvent>? OnTaskInsight;
    public event Action<ErrorEvent>? OnError;

    public HubConnectionState State => _connection.State;

    public SessionHubClient(IConfiguration config, NavigationManager navigation)
    {
        var hubUrl = ResolveHubUrl(config, navigation);
        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect([
                TimeSpan.Zero,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(3),
                TimeSpan.FromSeconds(5)
            ])
            .Build();

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
        _connection.On<ContainerLogEvent>("containerLog",
            e => OnContainerLog?.Invoke(e));
        _connection.On<TaskInsightEvent>("taskInsight",
            e => OnTaskInsight?.Invoke(e));
        _connection.On<ErrorEvent>("error",
            e => OnError?.Invoke(e));
    }

    private static string ResolveHubUrl(IConfiguration config, NavigationManager navigation)
        => BackendUrlResolver.ResolveHubUrl(config, navigation);

    public async Task ConnectAsync()
    {
        if (_started)
            return;

        await _connection.StartAsync();
        _started = true;
    }

    public async Task<string> CreateSessionAsync(
        string prompt,
        string workspacePath,
        string aiAssistant = "claude",
        string model = "auto",
        int modelPower = 0,
        string structuredOutputSchema = "",
        string workflowName = "full-orchestrate")
    {
        var result = await _connection.InvokeAsync<CreateSessionResult>(
            "CreateSession",
            prompt,
            workspacePath,
            aiAssistant,
            model,
            modelPower,
            aiAssistant,
            structuredOutputSchema,
            workflowName);
        return result?.SessionId ?? "";
    }

    private record CreateSessionResult(string SessionId, string WorkflowName);

    public async Task StopSessionAsync(string sessionId)
    {
        await _connection.InvokeAsync("StopSession", sessionId);
    }

    public async Task JoinSessionAsync(string sessionId)
    {
        await ConnectAsync();
        await _connection.InvokeAsync("JoinSession", sessionId);
    }

    public async Task LeaveSessionAsync(string sessionId)
    {
        if (!_started)
            return;

        await _connection.InvokeAsync("LeaveSession", sessionId);
    }

    public async Task ApproveAsync(string sessionId, bool approve)
    {
        await _connection.InvokeAsync("Approve", sessionId, approve);
    }

    public async ValueTask DisposeAsync()
    {
        if (_started)
            await _connection.DisposeAsync();
    }
}
