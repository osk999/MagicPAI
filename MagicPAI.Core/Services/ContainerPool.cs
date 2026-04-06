using System.Collections.Concurrent;
using MagicPAI.Core.Config;
using MagicPAI.Core.Models;
using Microsoft.Extensions.Logging;

namespace MagicPAI.Core.Services;

/// <summary>
/// Maintains a pool of pre-warmed containers/pods for instant workflow startup.
/// Works with any <see cref="IContainerManager"/> implementation (Docker or K8s).
/// </summary>
public class ContainerPool : IAsyncDisposable
{
    private readonly IContainerManager _containerManager;
    private readonly MagicPaiConfig _config;
    private readonly ILogger<ContainerPool> _logger;
    private readonly ConcurrentQueue<ContainerInfo> _pool = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _replenishLock = new(1, 1);
    private readonly int _poolSize;
    private Task? _replenishTask;
    private bool _disposed;

    public ContainerPool(IContainerManager containerManager, MagicPaiConfig config,
        ILogger<ContainerPool> logger)
    {
        _containerManager = containerManager;
        _config = config;
        _logger = logger;
        _poolSize = Math.Max(1, config.ContainerPoolSize);

        if (config.EnableContainerPool)
        {
            _replenishTask = ReplenishLoopAsync(_cts.Token);
        }
    }

    /// <summary>
    /// Current number of pre-warmed containers available in the pool.
    /// </summary>
    public int Available => _pool.Count;

    /// <summary>
    /// Acquire a pre-warmed container from the pool.
    /// If the pool is empty, spawns a new container on demand.
    /// </summary>
    public async Task<ContainerInfo> AcquireAsync(ContainerConfig config, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Try to get a pre-warmed container from the pool
        if (_pool.TryDequeue(out var container))
        {
            // Verify it's still running before handing it out
            if (await _containerManager.IsRunningAsync(container.ContainerId, ct))
            {
                _logger.LogInformation(
                    "Acquired pre-warmed container {ContainerId} from pool ({Remaining} remaining)",
                    container.ContainerId, _pool.Count);

                // Trigger background replenish
                _ = TriggerReplenishAsync();
                return container;
            }

            // Container died — discard and fall through to spawn a new one
            _logger.LogWarning("Pre-warmed container {ContainerId} is no longer running, spawning fresh",
                container.ContainerId);
        }

        // Pool empty or container was stale — spawn a new one
        _logger.LogInformation("No pre-warmed containers available, spawning on demand");
        return await _containerManager.SpawnAsync(config, ct);
    }

    /// <summary>
    /// Return a container to the pool for reuse, or destroy it if the pool is full.
    /// </summary>
    public async Task ReturnAsync(ContainerInfo container, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_pool.Count < _poolSize)
        {
            // Check it's still running before returning to pool
            if (await _containerManager.IsRunningAsync(container.ContainerId, ct))
            {
                _pool.Enqueue(container);
                _logger.LogDebug("Returned container {ContainerId} to pool ({Count}/{Max})",
                    container.ContainerId, _pool.Count, _poolSize);
                return;
            }
        }

        // Pool is full or container is dead — destroy it
        _logger.LogDebug("Destroying container {ContainerId} (pool full or container stopped)",
            container.ContainerId);
        await _containerManager.DestroyAsync(container.ContainerId, ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _logger.LogInformation("Disposing container pool, cleaning up {Count} containers", _pool.Count);

        await _cts.CancelAsync();

        if (_replenishTask != null)
        {
            try
            {
                await _replenishTask;
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
        }

        // Destroy all pooled containers
        while (_pool.TryDequeue(out var container))
        {
            try
            {
                await _containerManager.DestroyAsync(container.ContainerId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to destroy pooled container {ContainerId}",
                    container.ContainerId);
            }
        }

        _cts.Dispose();
        _replenishLock.Dispose();

        GC.SuppressFinalize(this);
    }

    // --- Private helpers ---

    private async Task TriggerReplenishAsync()
    {
        if (!await _replenishLock.WaitAsync(0)) return; // Already replenishing
        try
        {
            await ReplenishPoolAsync(_cts.Token);
        }
        finally
        {
            _replenishLock.Release();
        }
    }

    private async Task ReplenishLoopAsync(CancellationToken ct)
    {
        // Initial fill
        await ReplenishPoolAsync(ct);

        // Periodic check
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            await ReplenishPoolAsync(ct);
        }
    }

    private async Task ReplenishPoolAsync(CancellationToken ct)
    {
        var deficit = _poolSize - _pool.Count;
        if (deficit <= 0) return;

        _logger.LogDebug("Replenishing container pool: need {Deficit} containers", deficit);

        var defaultConfig = new ContainerConfig
        {
            Image = _config.WorkerImage,
            WorkspacePath = _config.WorkspacePath,
            ContainerWorkDir = _config.ContainerWorkDir,
            MemoryLimitMb = _config.DefaultMemoryLimitMb,
            CpuCount = _config.DefaultCpuCount,
            Timeout = TimeSpan.FromMinutes(_config.ContainerTimeoutMinutes)
        };

        for (var i = 0; i < deficit && !ct.IsCancellationRequested; i++)
        {
            try
            {
                var container = await _containerManager.SpawnAsync(defaultConfig, ct);
                _pool.Enqueue(container);
                _logger.LogDebug("Pre-warmed container {ContainerId} added to pool ({Count}/{Max})",
                    container.ContainerId, _pool.Count, _poolSize);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to pre-warm container for pool");
                // Don't fail the whole replenish — try the next one
            }
        }
    }
}
