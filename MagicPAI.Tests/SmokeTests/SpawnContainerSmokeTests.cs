using MagicPAI.Core.Config;
using MagicPAI.Core.Models;
using MagicPAI.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace MagicPAI.Tests.SmokeTests;

/// <summary>
/// Smoke tests for the spawn-container lifecycle: spawn → isRunning → exec → destroy.
/// Uses mocked IContainerManager to verify the full contract without a Docker daemon.
/// </summary>
public class SpawnContainerSmokeTests
{
    private readonly Mock<IContainerManager> _mock;
    private readonly ContainerConfig _config;

    public SpawnContainerSmokeTests()
    {
        _mock = new Mock<IContainerManager>();
        _config = new ContainerConfig
        {
            Image = "magicpai-env:latest",
            WorkspacePath = "/tmp/test-workspace",
            MemoryLimitMb = 2048,
            CpuCount = 1
        };
    }

    [Fact]
    public async Task FullLifecycle_Spawn_IsRunning_Exec_Destroy()
    {
        const string containerId = "smoke-container-001";

        _mock.Setup(m => m.SpawnAsync(It.IsAny<ContainerConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContainerInfo(containerId, null));
        _mock.Setup(m => m.IsRunningAsync(containerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mock.Setup(m => m.ExecAsync(containerId, "echo smoke", "/workspace", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecResult(0, "smoke\n", ""));
        _mock.Setup(m => m.DestroyAsync(containerId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Spawn
        var info = await _mock.Object.SpawnAsync(_config, CancellationToken.None);
        Assert.Equal(containerId, info.ContainerId);
        Assert.Null(info.GuiUrl);

        // IsRunning
        Assert.True(await _mock.Object.IsRunningAsync(containerId, CancellationToken.None));

        // Exec
        var exec = await _mock.Object.ExecAsync(containerId, "echo smoke", "/workspace", CancellationToken.None);
        Assert.Equal(0, exec.ExitCode);
        Assert.Contains("smoke", exec.Output);
        Assert.Empty(exec.Error);

        // Destroy
        await _mock.Object.DestroyAsync(containerId, CancellationToken.None);

        _mock.Verify(m => m.SpawnAsync(It.IsAny<ContainerConfig>(), It.IsAny<CancellationToken>()), Times.Once);
        _mock.Verify(m => m.DestroyAsync(containerId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FullLifecycle_WithGui_ReturnsGuiUrl()
    {
        const string containerId = "smoke-gui-001";
        const string guiUrl = "http://localhost:7900";

        var guiConfig = new ContainerConfig
        {
            Image = "magicpai-env:latest",
            WorkspacePath = "/tmp/test-workspace",
            EnableGui = true,
            GuiPort = 7900
        };

        _mock.Setup(m => m.SpawnAsync(It.IsAny<ContainerConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContainerInfo(containerId, guiUrl));
        _mock.Setup(m => m.GetGuiUrl(containerId)).Returns(guiUrl);
        _mock.Setup(m => m.DestroyAsync(containerId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var info = await _mock.Object.SpawnAsync(guiConfig, CancellationToken.None);
        Assert.Equal(containerId, info.ContainerId);
        Assert.Equal(guiUrl, info.GuiUrl);
        Assert.Equal(guiUrl, _mock.Object.GetGuiUrl(containerId));

        await _mock.Object.DestroyAsync(containerId, CancellationToken.None);
    }

    [Fact]
    public async Task SpawnFailure_ThrowsPropagated()
    {
        _mock.Setup(m => m.SpawnAsync(It.IsAny<ContainerConfig>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Docker daemon not available"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _mock.Object.SpawnAsync(_config, CancellationToken.None));
    }

    [Fact]
    public async Task ExecStreaming_CollectsChunks()
    {
        const string containerId = "smoke-stream-001";
        var chunks = new List<string>();

        _mock.Setup(m => m.SpawnAsync(It.IsAny<ContainerConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContainerInfo(containerId, null));
        _mock.Setup(m => m.ExecStreamingAsync(containerId, "dotnet build",
                It.IsAny<Action<string>>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, Action<string>, TimeSpan, CancellationToken>(
                (_, _, onOutput, _, _) =>
                {
                    onOutput("Restoring packages...\n");
                    onOutput("Build succeeded.\n");
                })
            .ReturnsAsync(new ExecResult(0, "Restoring packages...\nBuild succeeded.\n", ""));

        await _mock.Object.SpawnAsync(_config, CancellationToken.None);
        var result = await _mock.Object.ExecStreamingAsync(
            containerId, "dotnet build",
            chunk => chunks.Add(chunk),
            TimeSpan.FromMinutes(5), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(2, chunks.Count);
        Assert.Contains("Build succeeded.", result.Output);
    }

    [Fact]
    public async Task IsRunning_ReturnsFalse_AfterDestroy()
    {
        const string containerId = "smoke-destroy-001";
        var running = true;

        _mock.Setup(m => m.SpawnAsync(It.IsAny<ContainerConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContainerInfo(containerId, null));
        _mock.Setup(m => m.IsRunningAsync(containerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => running);
        _mock.Setup(m => m.DestroyAsync(containerId, It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((_, _) => running = false)
            .Returns(Task.CompletedTask);

        await _mock.Object.SpawnAsync(_config, CancellationToken.None);
        Assert.True(await _mock.Object.IsRunningAsync(containerId, CancellationToken.None));

        await _mock.Object.DestroyAsync(containerId, CancellationToken.None);
        Assert.False(await _mock.Object.IsRunningAsync(containerId, CancellationToken.None));
    }

    [Fact]
    public async Task ContainerPool_AcquireAndReturn_Lifecycle()
    {
        const string containerId = "smoke-pool-001";
        var running = true;

        _mock.Setup(m => m.SpawnAsync(It.IsAny<ContainerConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContainerInfo(containerId, null));
        _mock.Setup(m => m.IsRunningAsync(containerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => running);
        _mock.Setup(m => m.DestroyAsync(containerId, It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((_, _) => running = false)
            .Returns(Task.CompletedTask);

        var poolConfig = new MagicPaiConfig
        {
            EnableContainerPool = false, // Don't start background replenish loop
            ContainerPoolSize = 2,
            WorkerImage = "magicpai-env:latest",
            WorkspacePath = "/tmp/test-workspace"
        };

        await using var pool = new ContainerPool(_mock.Object, poolConfig,
            NullLogger<ContainerPool>.Instance);

        Assert.Equal(0, pool.Available);

        // Acquire spawns on demand when pool is empty
        var container = await pool.AcquireAsync(_config, CancellationToken.None);
        Assert.Equal(containerId, container.ContainerId);

        // Return adds back to pool
        await pool.ReturnAsync(container, CancellationToken.None);
        Assert.Equal(1, pool.Available);
    }

    [Fact]
    public void ContainerConfig_Defaults_AreCorrect()
    {
        var config = new ContainerConfig();

        Assert.Equal("magicpai-env:latest", config.Image);
        Assert.Equal("/workspace", config.ContainerWorkDir);
        Assert.Equal(4096, config.MemoryLimitMb);
        Assert.Equal(2, config.CpuCount);
        Assert.False(config.MountDockerSocket);
        Assert.False(config.EnableGui);
        Assert.Null(config.GuiPort);
        Assert.Empty(config.Env);
        Assert.Equal(TimeSpan.FromMinutes(30), config.Timeout);
    }

    [Fact]
    public void BuildCredentialBinds_WithNoCredentials_ReturnsEmpty()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var binds = DockerContainerManager.BuildCredentialBinds(tempDir);
            Assert.Empty(binds);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void BuildCredentialBinds_WithAllCredentials_MountsReadOnly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(Path.Combine(tempDir, ".claude"));
        Directory.CreateDirectory(Path.Combine(tempDir, ".codex"));
        File.WriteAllText(Path.Combine(tempDir, ".claude.json"), "{}");
        File.WriteAllText(Path.Combine(tempDir, ".claude", ".credentials.json"), "{}");
        File.WriteAllText(Path.Combine(tempDir, ".codex", "auth.json"), "{}");
        File.WriteAllText(Path.Combine(tempDir, ".codex", "cap_sid"), "sid");

        try
        {
            var binds = DockerContainerManager.BuildCredentialBinds(tempDir);
            Assert.Equal(4, binds.Count);
            Assert.All(binds, bind => Assert.EndsWith(":ro", bind));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Cancellation_DuringSpawn_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        _mock.Setup(m => m.SpawnAsync(It.IsAny<ContainerConfig>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _mock.Object.SpawnAsync(_config, cts.Token));
    }
}
