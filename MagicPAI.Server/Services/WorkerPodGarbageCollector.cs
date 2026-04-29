using MagicPAI.Core.Config;
using MagicPAI.Core.Models;
using MagicPAI.Core.Services;
using MagicPAI.Server.Bridge;

namespace MagicPAI.Server.Services;

/// <summary>
/// Background service that periodically cleans up orphaned worker pods/containers
/// whose sessions have completed, failed, or been cancelled.
///
/// Two-pass design:
///   1. <b>Tracker sweep</b>: iterate <see cref="SessionTracker"/> and destroy
///      containers whose session reached a terminal state.
///   2. <b>Label fallback sweep</b>: query the container engine for any
///      container carrying the <c>magicpai.session</c> label whose owning
///      session is unknown to the tracker (server restart) or is in a terminal
///      state with the container still running. This recovers from BUG-4 where
///      the in-memory tracker was lost across a restart and orphan containers
///      survived indefinitely.
/// </summary>
public class WorkerPodGarbageCollector : BackgroundService
{
    /// <summary>
    /// Label key attached to every MagicPAI-spawned container at create time
    /// (see <c>DockerActivities.SpawnAsync</c> and
    /// <c>DockerContainerManager.ConfigureCreateStartInfo</c>). Operators can
    /// inspect leaked containers with:
    /// <code>docker ps -a --filter label=magicpai.session</code>
    /// </summary>
    public const string SessionLabel = "magicpai.session";

    /// <summary>
    /// Sessions terminal states recognised by the GC. Containers whose owning
    /// session is in one of these states are eligible for reap.
    /// </summary>
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "completed", "failed", "cancelled", "terminated"
    };

    private readonly IContainerManager _containerManager;
    private readonly SessionTracker _sessionTracker;
    private readonly MagicPaiConfig _config;
    private readonly ILogger<WorkerPodGarbageCollector> _logger;

    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Grace period before a label-only orphan is considered reapable. Avoids
    /// racing with newly-spawned containers that haven't yet been registered
    /// with <see cref="SessionTracker"/>.
    /// </summary>
    private static readonly TimeSpan OrphanGracePeriod = TimeSpan.FromMinutes(5);

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
            "WorkerPodGarbageCollector started (backend={Backend}, interval={Interval}s, orphanGrace={Grace}s)",
            _config.ExecutionBackend, ScanInterval.TotalSeconds, OrphanGracePeriod.TotalSeconds);

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

    /// <summary>
    /// Runs one full GC pass. Public so the unit tests can drive the loop
    /// without spinning a real BackgroundService.
    /// </summary>
    public async Task ScanAndCleanupAsync(CancellationToken ct)
    {
        var trackerCleaned = await SweepTrackedSessionsAsync(ct);
        var labelCleaned = await SweepLabeledOrphansAsync(ct);

        var total = trackerCleaned + labelCleaned;
        if (total > 0)
            _logger.LogInformation(
                "GC: Cleaned up {Total} orphaned workers (tracker={Tracker}, labelFallback={Label})",
                total, trackerCleaned, labelCleaned);
    }

    /// <summary>
    /// Pass 1: iterate the in-memory tracker. Reaps containers whose session
    /// reached a terminal state but whose <c>finally</c> block did not destroy
    /// the container (e.g. non-cooperative termination, cancelled destroy
    /// activity).
    /// </summary>
    private async Task<int> SweepTrackedSessionsAsync(CancellationToken ct)
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
            if (!TerminalStates.Contains(session.State))
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

        return cleaned;
    }

    /// <summary>
    /// Pass 2 (BUG-4 fix): list every container with the <c>magicpai.session</c>
    /// label and reap orphans the in-memory tracker no longer knows about (or
    /// that match a terminal session in the tracker yet are still running).
    ///
    /// Safety:
    ///   - Only containers older than <see cref="OrphanGracePeriod"/> are
    ///     considered, to avoid racing newly-spawned containers that haven't
    ///     yet been registered with the tracker.
    ///   - Containers without the label are never touched (they're not ours).
    ///   - Containers belonging to an active, non-terminal session are skipped.
    /// </summary>
    private async Task<int> SweepLabeledOrphansAsync(CancellationToken ct)
    {
        IReadOnlyList<LabeledContainer> labeled;
        try
        {
            labeled = await _containerManager.ListContainersByLabelAsync(SessionLabel, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GC: Label fallback sweep failed to list containers");
            return 0;
        }

        if (labeled.Count == 0)
            return 0;

        var nowUtc = DateTime.UtcNow;
        var cleaned = 0;

        foreach (var container in labeled)
        {
            if (!container.Labels.TryGetValue(SessionLabel, out var sessionId)
                || string.IsNullOrEmpty(sessionId))
            {
                // Filter says label exists but value missing — engine quirk; skip.
                continue;
            }

            // Grace period: a container that was just spawned might not yet be
            // registered with the in-memory tracker. Don't kill it.
            var age = nowUtc - container.CreatedAtUtc;
            if (age < OrphanGracePeriod)
                continue;

            // Decide whether this container is an orphan.
            var session = _sessionTracker.GetSession(sessionId);
            var sessionUnknown = session is null;
            var sessionTerminal = session is not null && TerminalStates.Contains(session.State);

            // We only reap if the session is unknown (server restart) or
            // already terminal — never an active session whose container is
            // legitimately running.
            if (!sessionUnknown && !sessionTerminal)
                continue;

            // For known-terminal sessions the tracker sweep already tried
            // (and may have destroyed) the container. Only chase it here if
            // it's still running.
            if (sessionTerminal && !container.IsRunning)
                continue;

            var reason = sessionUnknown ? "session unknown to tracker" : "session terminal but container running";
            _logger.LogWarning(
                "GC: Reaping orphan container {ContainerId} (sessionId={SessionId} age={AgeMinutes}m running={Running} reason={Reason})",
                container.ContainerId, sessionId, (int)age.TotalMinutes, container.IsRunning, reason);

            try
            {
                await _containerManager.DestroyAsync(container.ContainerId, ct);
                cleaned++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "GC: Failed to destroy orphan container {ContainerId} (sessionId={SessionId})",
                    container.ContainerId, sessionId);
            }
        }

        return cleaned;
    }
}
