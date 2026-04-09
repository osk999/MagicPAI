using MagicPAI.Core.Config;
using MagicPAI.Core.Models;
using MagicPAI.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace MagicPAI.Tests.Services;

/// <summary>
/// Unit tests for <see cref="ContainerPool"/> covering:
/// - On-demand spawn when pool is empty
/// - Pre-warmed container reuse
/// - Stale (non-running) container discarded, fresh spawn triggered
/// - Return of running container adds it to pool
/// - Return of dead container destroys it immediately
/// - Pool-size overflow trims excess containers
/// - DisposeAsync destroys all pooled containers
/// - ObjectDisposedException on acquire/return after dispose
/// - Replenish errors are swallowed (pool stays healthy)
/// </summary>
public class ContainerPoolTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static ContainerPool CreatePool(
        IContainerManager manager,
        int poolSize = 3,
        bool enableReplenish = false) =>
        new(manager, new MagicPaiConfig
        {
            EnableContainerPool   = enableReplenish,
            ContainerPoolSize     = poolSize,
            WorkerImage           = "magicpai-env:latest",
            WorkspacePath         = "/tmp/test",
            ContainerWorkDir      = "/workspace",
            DefaultMemoryLimitMb  = 2048,
            DefaultCpuCount       = 1,
            ContainerTimeoutMinutes = 30
        }, NullLogger<ContainerPool>.Instance);

    private static ContainerConfig BaseConfig() => new()
    {
        Image         = "magicpai-env:latest",
        WorkspacePath = "/tmp/test",
        MemoryLimitMb = 2048,
        CpuCount      = 1
    };

    // -----------------------------------------------------------------------
    // Initial state
    // -----------------------------------------------------------------------

    [Fact]
    public void Available_StartsAtZero_WithReplenishDisabled()
    {
        var mock = new Mock<IContainerManager>();
        var pool = CreatePool(mock.Object);

        Assert.Equal(0, pool.Available);
    }

    // -----------------------------------------------------------------------
    // Acquire – empty pool spawns on demand
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AcquireAsync_EmptyPool_SpawnsNewContainer()
    {
        const string cid = "pool-spawn-001";
        var mock = new Mock<IContainerManager>();
        mock.Setup(m => m.SpawnAsync(It.IsAny<ContainerConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContainerInfo(cid, null));

        await using var pool = CreatePool(mock.Object);

        var container = await pool.AcquireAsync(BaseConfig(), CancellationToken.None);

        Assert.Equal(cid, container.ContainerId);
        mock.Verify(m => m.SpawnAsync(It.IsAny<ContainerConfig>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // -----------------------------------------------------------------------
    // Acquire – pre-warmed container reused
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AcquireAsync_PoolHasContainer_ReturnsPrewarmed_NoExtraSpawn()
    {
        const string cid = "pool-prewarmed-001";
        var mock = new Mock<IContainerManager>();

        // IsRunning must return true for the return, and again for the re-acquire
        mock.Setup(m => m.IsRunningAsync(cid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Spawn is called once when we first populate via AcquireAsync (empty pool)
        mock.Setup(m => m.SpawnAsync(It.IsAny<ContainerConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContainerInfo(cid, null));

        await using var pool = CreatePool(mock.Object);

        // First acquire spawns; return puts it back in pool
        var first = await pool.AcquireAsync(BaseConfig(), CancellationToken.None);
        Assert.Equal(0, pool.Available);

        await pool.ReturnAsync(first, CancellationToken.None);
        Assert.Equal(1, pool.Available);

        // Second acquire should re-use the pre-warmed container, then trigger background replenish
        var second = await pool.AcquireAsync(BaseConfig(), CancellationToken.None);
        Assert.Equal(cid, second.ContainerId);

        // 1 initial spawn (on-demand when pool empty) + up to poolSize spawns from background replenish
        mock.Verify(m => m.SpawnAsync(It.IsAny<ContainerConfig>(), It.IsAny<CancellationToken>()),
            Times.Between(1, 1 + 3, Moq.Range.Inclusive));
    }

    // -----------------------------------------------------------------------
    // Acquire – stale container discarded, fresh spawn
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AcquireAsync_StaleContainerInPool_DiscardsAndSpawnsFresh()
    {
        const string staleCid = "pool-stale-001";
        const string freshCid = "pool-fresh-001";

        var mock = new Mock<IContainerManager>();

        // Stale container was returned when running, then died
        var isRunningCallCount = 0;
        mock.Setup(m => m.IsRunningAsync(staleCid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                // First call (during ReturnAsync): container is alive → gets pooled
                // Second call (during AcquireAsync): container has died → discarded
                return ++isRunningCallCount == 1;
            });

        mock.SetupSequence(m => m.SpawnAsync(It.IsAny<ContainerConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContainerInfo(staleCid, null))   // first call populates pool
            .ReturnsAsync(new ContainerInfo(freshCid, null));  // second call after stale discarded

        await using var pool = CreatePool(mock.Object);

        // Acquire + return → stale container in pool
        var initial = await pool.AcquireAsync(BaseConfig(), CancellationToken.None);
        await pool.ReturnAsync(initial, CancellationToken.None);
        Assert.Equal(1, pool.Available);

        // Acquire again → stale detected → fresh spawn
        var acquired = await pool.AcquireAsync(BaseConfig(), CancellationToken.None);
        Assert.Equal(freshCid, acquired.ContainerId);

        mock.Verify(m => m.SpawnAsync(It.IsAny<ContainerConfig>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    // -----------------------------------------------------------------------
    // Return – running container added to pool
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ReturnAsync_RunningContainer_PoolSizeIncreases()
    {
        const string cid = "pool-return-001";
        var mock = new Mock<IContainerManager>();
        mock.Setup(m => m.IsRunningAsync(cid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await using var pool = CreatePool(mock.Object, poolSize: 5);

        Assert.Equal(0, pool.Available);
        await pool.ReturnAsync(new ContainerInfo(cid, null), CancellationToken.None);
        Assert.Equal(1, pool.Available);
    }

    // -----------------------------------------------------------------------
    // Return – dead container is destroyed, not pooled
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ReturnAsync_DeadContainer_DestroyedImmediately_NotPooled()
    {
        const string cid = "pool-dead-001";
        var mock = new Mock<IContainerManager>();
        mock.Setup(m => m.IsRunningAsync(cid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        mock.Setup(m => m.DestroyAsync(cid, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await using var pool = CreatePool(mock.Object);

        await pool.ReturnAsync(new ContainerInfo(cid, null), CancellationToken.None);

        Assert.Equal(0, pool.Available);
        mock.Verify(m => m.DestroyAsync(cid, It.IsAny<CancellationToken>()), Times.Once);
    }

    // -----------------------------------------------------------------------
    // Return – pool overflow trims excess
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ReturnAsync_PoolAtCapacity_ExcessContainerDestroyed()
    {
        const int poolSize = 2;
        var destroyedIds = new List<string>();

        var mock = new Mock<IContainerManager>();
        for (var i = 1; i <= 3; i++)
        {
            var id = $"pool-overflow-{i:000}";
            mock.Setup(m => m.IsRunningAsync(id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
        }
        mock.Setup(m => m.DestroyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((id, _) => destroyedIds.Add(id))
            .Returns(Task.CompletedTask);

        await using var pool = CreatePool(mock.Object, poolSize: poolSize);

        for (var i = 1; i <= 3; i++)
            await pool.ReturnAsync(new ContainerInfo($"pool-overflow-{i:000}", null), CancellationToken.None);

        // Pool should be capped at poolSize
        Assert.Equal(poolSize, pool.Available);

        // One container must have been destroyed to make room
        Assert.Single(destroyedIds);
    }

    // -----------------------------------------------------------------------
    // DisposeAsync – destroys all pooled containers
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DisposeAsync_DestroysAllPooledContainers()
    {
        var cids = new[] { "pool-dispose-001", "pool-dispose-002", "pool-dispose-003" };
        var destroyedIds = new List<string>();

        var mock = new Mock<IContainerManager>();
        foreach (var cid in cids)
        {
            mock.Setup(m => m.IsRunningAsync(cid, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
        }
        mock.Setup(m => m.DestroyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((id, _) => destroyedIds.Add(id))
            .Returns(Task.CompletedTask);

        var pool = CreatePool(mock.Object, poolSize: 10);

        foreach (var cid in cids)
            await pool.ReturnAsync(new ContainerInfo(cid, null), CancellationToken.None);

        Assert.Equal(3, pool.Available);

        await pool.DisposeAsync();

        Assert.Equal(3, destroyedIds.Count);
        foreach (var cid in cids)
            Assert.Contains(cid, destroyedIds);
    }

    [Fact]
    public async Task DisposeAsync_DestroyFailure_DoesNotPreventOtherContainerCleanup()
    {
        var mock = new Mock<IContainerManager>();
        var cleanedUp = new List<string>();

        mock.Setup(m => m.IsRunningAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // First container's destroy throws
        mock.Setup(m => m.DestroyAsync("failing-ctr", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("forced destroy failure"));

        mock.Setup(m => m.DestroyAsync("ok-ctr-1", It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((id, _) => cleanedUp.Add(id))
            .Returns(Task.CompletedTask);

        mock.Setup(m => m.DestroyAsync("ok-ctr-2", It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((id, _) => cleanedUp.Add(id))
            .Returns(Task.CompletedTask);

        var pool = CreatePool(mock.Object, poolSize: 10);

        await pool.ReturnAsync(new ContainerInfo("ok-ctr-1",    null), CancellationToken.None);
        await pool.ReturnAsync(new ContainerInfo("failing-ctr", null), CancellationToken.None);
        await pool.ReturnAsync(new ContainerInfo("ok-ctr-2",    null), CancellationToken.None);

        // Dispose must not throw even though one destroy fails
        await pool.DisposeAsync();

        // The two containers that didn't throw should still be destroyed
        Assert.Contains("ok-ctr-1", cleanedUp);
        Assert.Contains("ok-ctr-2", cleanedUp);
    }

    // -----------------------------------------------------------------------
    // Post-dispose behaviour
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AcquireAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var mock = new Mock<IContainerManager>();
        var pool = CreatePool(mock.Object);

        await pool.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => pool.AcquireAsync(BaseConfig(), CancellationToken.None));
    }

    [Fact]
    public async Task ReturnAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var mock = new Mock<IContainerManager>();
        var pool = CreatePool(mock.Object);

        await pool.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => pool.ReturnAsync(new ContainerInfo("c1", null), CancellationToken.None));
    }

    [Fact]
    public async Task DisposeAsync_Idempotent_SecondCallDoesNotThrow()
    {
        var mock = new Mock<IContainerManager>();
        var pool = CreatePool(mock.Object);

        await pool.DisposeAsync();
        await pool.DisposeAsync(); // second call must be a no-op
    }

    // -----------------------------------------------------------------------
    // Pool size boundary
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Available_ReflectsCurrentPoolSize_AcrossReturnAndAcquire()
    {
        const string cid = "pool-count-001";
        var mock = new Mock<IContainerManager>();
        mock.Setup(m => m.IsRunningAsync(cid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        mock.Setup(m => m.SpawnAsync(It.IsAny<ContainerConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContainerInfo(cid, null));

        await using var pool = CreatePool(mock.Object);

        Assert.Equal(0, pool.Available);

        await pool.ReturnAsync(new ContainerInfo(cid, null), CancellationToken.None);
        Assert.Equal(1, pool.Available);

        await pool.AcquireAsync(BaseConfig(), CancellationToken.None);
        // After acquire the pool triggers background replenish; count is between 0 and poolSize
        Assert.InRange(pool.Available, 0, 3);
    }

    [Fact]
    public async Task PoolSize_ConfiguredToOne_NeverExceedsOne()
    {
        var mock = new Mock<IContainerManager>();
        var destroyedCount = 0;

        for (var i = 1; i <= 5; i++)
        {
            var id = $"cap-ctr-{i:000}";
            mock.Setup(m => m.IsRunningAsync(id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
        }
        mock.Setup(m => m.DestroyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((_, _) => destroyedCount++)
            .Returns(Task.CompletedTask);

        await using var pool = CreatePool(mock.Object, poolSize: 1);

        for (var i = 1; i <= 5; i++)
            await pool.ReturnAsync(new ContainerInfo($"cap-ctr-{i:000}", null), CancellationToken.None);

        Assert.Equal(1, pool.Available);
        Assert.Equal(4, destroyedCount); // 4 overflow containers destroyed
    }
}
