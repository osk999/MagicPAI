using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using MagicPAI.Shared.Models;

namespace MagicPAI.Studio.Services;

/// <summary>
/// Connects to the MagicPAI SignalR hub and exposes workflow progress events
/// for live visualization in Studio components.
/// </summary>
public sealed class WorkflowInstanceLiveUpdater : IAsyncDisposable
{
    private readonly HubConnection _connection;
    private readonly HashSet<string> _subscribedSessions = new();
    private bool _started;

    public event Action<string, string, string>? OnActivityStatusChanged;
    public event Action<string, string>? OnOutputChunkReceived;
    public event Action<string, int, int>? OnWorkflowProgressUpdated;
    public event Action<string, string>? OnSessionStateChanged;
    public event Action<string, string>? OnErrorReceived;

    public HubConnectionState State => _connection.State;

    public WorkflowInstanceLiveUpdater(IConfiguration config, NavigationManager navigation)
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

        _connection.On<WorkflowProgressEvent>("workflowProgress", e =>
        {
            if (!_subscribedSessions.Contains(e.SessionId))
                return;

            OnActivityStatusChanged?.Invoke(e.SessionId, e.ActivityName, e.Status);
            OnWorkflowProgressUpdated?.Invoke(e.SessionId, e.CompletedSteps, e.TotalSteps);
        });

        _connection.On<OutputChunkEvent>("outputChunk", e =>
        {
            if (!_subscribedSessions.Contains(e.SessionId))
                return;

            OnOutputChunkReceived?.Invoke(e.SessionId, e.Text);
        });

        _connection.On<SessionStateEvent>("sessionStateChanged", e =>
        {
            if (!_subscribedSessions.Contains(e.SessionId))
                return;

            OnSessionStateChanged?.Invoke(e.SessionId, e.State);
        });

        _connection.On<ErrorEvent>("error", e =>
        {
            if (!_subscribedSessions.Contains(e.SessionId))
                return;

            OnErrorReceived?.Invoke(e.SessionId, e.Message);
        });
    }

    private static string ResolveHubUrl(IConfiguration config, NavigationManager navigation)
        => BackendUrlResolver.ResolveHubUrl(config, navigation);

    public async Task StartAsync(string? hubUrl = null)
    {
        if (_started)
            return;

        await _connection.StartAsync();
        _started = true;
    }

    public Task SubscribeToSession(string sessionId)
    {
        _subscribedSessions.Add(sessionId);
        return Task.CompletedTask;
    }

    public Task UnsubscribeFromSession(string sessionId)
    {
        _subscribedSessions.Remove(sessionId);
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _subscribedSessions.Clear();
        if (_started)
            await _connection.DisposeAsync();
    }
}
