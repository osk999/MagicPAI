using System.Collections.Concurrent;
using MagicPAI.Core.Services;
using MagicPAI.Server.Bridge;
using MagicPAI.Server.Hubs;
using MagicPAI.Shared.Models;
using Microsoft.AspNetCore.SignalR;

namespace MagicPAI.Server.Services;

/// <summary>
/// Streams worker container logs into the owning session until the container is destroyed
/// or the stream ends. This keeps container telemetry separate from Elsa activity logs while
/// still surfacing it through the same session UI.
/// </summary>
public class SessionContainerLogStreamer : ISessionContainerLogStreamer
{
    private readonly IContainerManager _containerManager;
    private readonly SessionTracker _tracker;
    private readonly IHubContext<SessionHub> _hubContext;
    private readonly ILogger<SessionContainerLogStreamer> _logger;
    private readonly ConcurrentDictionary<string, StreamingRegistration> _registrations = new();

    public SessionContainerLogStreamer(
        IContainerManager containerManager,
        SessionTracker tracker,
        IHubContext<SessionHub> hubContext,
        ILogger<SessionContainerLogStreamer> logger)
    {
        _containerManager = containerManager;
        _tracker = tracker;
        _hubContext = hubContext;
        _logger = logger;
    }

    public void StartStreaming(string sessionId, string containerId)
    {
        var registration = new StreamingRegistration(containerId, new CancellationTokenSource());
        var existing = _registrations.AddOrUpdate(sessionId, registration, (_, current) =>
        {
            if (string.Equals(current.ContainerId, containerId, StringComparison.Ordinal))
                return current;

            current.Cancellation.Cancel();
            current.Cancellation.Dispose();
            return registration;
        });

        if (!ReferenceEquals(existing, registration))
            return;

        registration.Task = Task.Run(async () =>
        {
            try
            {
                await _containerManager.StreamLogsAsync(containerId, line =>
                {
                    if (string.IsNullOrWhiteSpace(line))
                        return;

                    _tracker.AppendContainerLog(sessionId, line);
                    var payload = new ContainerLogEvent(sessionId, containerId, line, DateTime.UtcNow);
                    _ = _hubContext.Clients.Group(sessionId).SendAsync("containerLog", payload);
                }, registration.Cancellation.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex,
                    "Container log stream ended with error for session {SessionId} container {ContainerId}",
                    sessionId,
                    containerId);
            }
            finally
            {
                if (_registrations.TryGetValue(sessionId, out var current) && ReferenceEquals(current, registration))
                    _registrations.TryRemove(sessionId, out _);

                registration.Cancellation.Dispose();
            }
        });
    }

    public async Task StopStreamingAsync(string sessionId)
    {
        if (!_registrations.TryRemove(sessionId, out var registration))
            return;

        registration.Cancellation.Cancel();
        try
        {
            if (registration.Task is not null)
                await registration.Task;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private sealed class StreamingRegistration
    {
        public StreamingRegistration(string containerId, CancellationTokenSource cancellation)
        {
            ContainerId = containerId;
            Cancellation = cancellation;
        }

        public string ContainerId { get; }
        public CancellationTokenSource Cancellation { get; }
        public Task? Task { get; set; }
    }
}
