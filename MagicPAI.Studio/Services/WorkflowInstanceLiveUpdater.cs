using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using MagicPAI.Core.Models;

namespace MagicPAI.Studio.Services;

/// <summary>
/// Connects to the MagicPAI SignalR hub and exposes workflow progress events
/// for live visualization in Elsa Studio components.
/// Wraps its own HubConnection so it can manage per-session subscriptions
/// independently of the main SessionHubClient.
/// </summary>
public sealed class WorkflowInstanceLiveUpdater : IAsyncDisposable
{
    private readonly HubConnection _connection;
    private readonly HashSet<string> _subscribedSessions = new();
    private bool _started;

    // Events for Blazor component binding
    public event Action<string, string, string>? OnActivityStatusChanged;   // sessionId, activityName, status
    public event Action<string, string>? OnOutputChunkReceived;             // sessionId, text
    public event Action<string, int, int>? OnWorkflowProgressUpdated;       // sessionId, completedSteps, totalSteps
    public event Action<string, string>? OnSessionStateChanged;             // sessionId, state
    public event Action<string, string>? OnErrorReceived;                   // sessionId, message

    public HubConnectionState State => _connection.State;

    public WorkflowInstanceLiveUpdater(IConfiguration config)
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

        _connection.On<WorkflowProgressEvent>("workflowProgress", e =>
        {
            if (!_subscribedSessions.Contains(e.SessionId)) return;
            OnActivityStatusChanged?.Invoke(e.SessionId, e.ActivityName, e.Status);
            OnWorkflowProgressUpdated?.Invoke(e.SessionId, e.CompletedSteps, e.TotalSteps);
        });

        _connection.On<OutputChunkEvent>("outputChunk", e =>
        {
            if (!_subscribedSessions.Contains(e.SessionId)) return;
            OnOutputChunkReceived?.Invoke(e.SessionId, e.Text);
        });

        _connection.On<SessionStateEvent>("sessionStateChanged", e =>
        {
            if (!_subscribedSessions.Contains(e.SessionId)) return;
            OnSessionStateChanged?.Invoke(e.SessionId, e.State);
        });

        _connection.On<ErrorEvent>("error", e =>
        {
            if (!_subscribedSessions.Contains(e.SessionId)) return;
            OnErrorReceived?.Invoke(e.SessionId, e.Message);
        });
    }

    /// <summary>Start the SignalR connection if not already connected.</summary>
    public async Task StartAsync(string? hubUrl = null)
    {
        if (_started) return;
        await _connection.StartAsync();
        _started = true;
    }

    /// <summary>Subscribe to live updates for a specific session.</summary>
    public Task SubscribeToSession(string sessionId)
    {
        _subscribedSessions.Add(sessionId);
        return Task.CompletedTask;
    }

    /// <summary>Unsubscribe from live updates for a specific session.</summary>
    public Task UnsubscribeFromSession(string sessionId)
    {
        _subscribedSessions.Remove(sessionId);
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _subscribedSessions.Clear();
        if (_started)
        {
            await _connection.DisposeAsync();
        }
    }
}
