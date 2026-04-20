using MagicPAI.Core.Config;
using MagicPAI.Core.Services;
using MagicPAI.Server.Bridge;

namespace MagicPAI.Server.Services;

/// <summary>
/// Background service that periodically cleans up orphaned worker pods/containers
/// whose sessions have completed, failed, or been cancelled.
/// </summary>
public class WorkerPodGarbageCollector : BackgroundService
{
    private readonly IContainerManager _containerManager;
    private readonly SessionTracker _sessionTracker;
    private readonly MagicPaiConfig _config;
    private readonly ILogger<WorkerPodGarbageCollector> _logger;

    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(60);

    public WorkerPodGarbageCollector(
        IContainerManager containerManager,
        SessionTracker sessionTracker,
        MagicPaiConfig config,
        ILogger<WorkerPodGarbageCollector> logger)
    {
        _containerManager = containerManager;
        _sessionTracker = sessionTracker;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "WorkerPodGarbageCollector started (backend={Backend}, interval={Interval}s)",
            _config.ExecutionBackend, ScanInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(ScanInterval, stoppingToken);
                await ScanAndCleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during worker garbage collection scan");
            }
        }

        _logger.LogInformation("WorkerPodGarbageCollector stopped");
    }

    private async Task ScanAndCleanupAsync(CancellationToken ct)
    {
        var sessions = _sessionTracker.GetAllSessions();
        var cleaned = 0;

        foreach (var session in sessions)
        {
            // Only clean up terminal sessions. "terminated" is included because
            // Temporal's terminate is non-cooperative — the workflow's `finally`
            // block doesn't get to destroy the container, so the GC sweeps up.
            // "cancelled" is included too for completeness; cancellation normally
            // lets finally run, but if the destroy activity itself gets cancelled,
            // the container can leak and GC picks it up on the next scan.
            if (session.State is not ("completed" or "failed" or "cancelled" or "terminated"))
                continue;

            // Need a container ID to clean up
            if (string.IsNullOrEmpty(session.ContainerId))
                continue;

            try
            {
                var running = await _containerManager.IsRunningAsync(session.ContainerId, ct);
                if (running)
                {
                    _logger.LogInformation(
                        "GC: Destroying orphaned {Backend} worker {ContainerId} for session {SessionId} (state={State})",
                        _config.ExecutionBackend, session.ContainerId, session.Id, session.State);
                    await _containerManager.DestroyAsync(session.ContainerId, ct);
                    cleaned++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "GC: Failed to check/destroy worker {ContainerId}", session.ContainerId);
            }
        }

        if (cleaned > 0)
            _logger.LogInformation("GC: Cleaned up {Count} orphaned workers", cleaned);
    }
}
