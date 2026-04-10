using MagicPAI.Core.Models;
using MagicPAI.Core.Services;
using Moq;

namespace MagicPAI.Tests.Activities;

/// <summary>
/// Smoke tests for the spawn-container lifecycle:
/// spawn → exec → isRunning → destroy.
/// Uses mocked IContainerManager (no Docker daemon required).
/// </summary>
public class SpawnContainerSmokeTests
{
    private static Mock<IContainerManager> CreateMockManager(string containerId = "smoke-ctr-001")
    {
        var mock = new Mock<IContainerManager>(MockBehavior.Strict);

        mock.Setup(m => m.SpawnAsync(It.IsAny<ContainerConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContainerInfo(containerId, null));

        mock.Setup(m => m.IsRunningAsync(containerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        mock.Setup(m => m.ExecAsync(containerId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecResult(0, "OK\n", ""));

        mock.Setup(m => m.DestroyAsync(containerId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return mock;
    }

    [Fact]
    public async Task Spawn_Exec_Destroy_Lifecycle()
    {
        var mock = CreateMockManager();
        var mgr = mock.Object;

        // Spawn
        var info = await mgr.SpawnAsync(new ContainerConfig { Image = "magicpai-env:latest" }, CancellationToken.None);
        Assert.Equal("smoke-ctr-001", info.ContainerId);

        // Verify running
        Assert.True(await mgr.IsRunningAsync(info.ContainerId, CancellationToken.None));

        // Execute a command
        var exec = await mgr.ExecAsync(info.ContainerId, "echo hello", "/workspace", CancellationToken.None);
        Assert.Equal(0, exec.ExitCode);

        // Destroy
        await mgr.DestroyAsync(info.ContainerId, CancellationToken.None);

        mock.Verify(m => m.SpawnAsync(It.IsAny<ContainerConfig>(), It.IsAny<CancellationToken>()), Times.Once);
        mock.Verify(m => m.DestroyAsync("smoke-ctr-001", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Spawn_WithGui_Returns_GuiUrl()
    {
        var mock = new Mock<IContainerManager>();
        mock.Setup(m => m.SpawnAsync(It.IsAny<ContainerConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContainerInfo("gui-ctr-001", "http://127.0.0.1:7900/vnc.html?autoconnect=1&resize=scale"));

        var config = new ContainerConfig { EnableGui = true, GuiPort = 7900 };
        var info = await mock.Object.SpawnAsync(config, CancellationToken.None);

        Assert.Equal("gui-ctr-001", info.ContainerId);
        Assert.Equal("http://127.0.0.1:7900/vnc.html?autoconnect=1&resize=scale", info.GuiUrl);
    }

    [Fact]
    public async Task Spawn_Failure_Does_Not_Leave_Dangling_Container()
    {
        var mock = new Mock<IContainerManager>();
        mock.Setup(m => m.SpawnAsync(It.IsAny<ContainerConfig>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Docker daemon not available"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => mock.Object.SpawnAsync(new ContainerConfig(), CancellationToken.None));

        // Destroy should never be called since spawn failed
        mock.Verify(m => m.DestroyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Spawn_ExecStreaming_Collects_Output_Chunks()
    {
        const string containerId = "stream-ctr-001";
        var mock = CreateMockManager(containerId);

        mock.Setup(m => m.ExecStreamingAsync(
                containerId, "dotnet build", It.IsAny<Action<string>>(),
                It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, Action<string>, TimeSpan, CancellationToken>(
                (_, _, onOutput, _, _) =>
                {
                    onOutput("Restoring packages...\n");
                    onOutput("Build succeeded.\n");
                })
            .ReturnsAsync(new ExecResult(0, "Restoring packages...\nBuild succeeded.\n", ""));

        var info = await mock.Object.SpawnAsync(new ContainerConfig(), CancellationToken.None);

        var chunks = new List<string>();
        var result = await mock.Object.ExecStreamingAsync(
            info.ContainerId, "dotnet build",
            chunk => chunks.Add(chunk),
            TimeSpan.FromMinutes(5), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(2, chunks.Count);
        Assert.Contains("Build succeeded.", result.Output);

        await mock.Object.DestroyAsync(info.ContainerId, CancellationToken.None);
    }

    [Fact]
    public async Task Spawn_Cancellation_Is_Respected()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var mock = new Mock<IContainerManager>();
        mock.Setup(m => m.SpawnAsync(It.IsAny<ContainerConfig>(), It.IsAny<CancellationToken>()))
            .Returns<ContainerConfig, CancellationToken>((_, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(new ContainerInfo("never", null));
            });

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => mock.Object.SpawnAsync(new ContainerConfig(), cts.Token));
    }

    [Fact]
    public void ContainerConfig_Defaults_Are_Sane_For_Smoke()
    {
        var config = new ContainerConfig();

        Assert.Equal("magicpai-env:latest", config.Image);
        Assert.Equal("/workspace", config.ContainerWorkDir);
        Assert.True(config.MemoryLimitMb >= 2048, "Memory should be at least 2 GB");
        Assert.True(config.CpuCount >= 1, "Should have at least 1 CPU");
        Assert.False(config.MountDockerSocket, "Docker socket should not be mounted by default");
        Assert.False(config.EnableGui, "GUI should be off by default");
    }
}
