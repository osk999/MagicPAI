using MagicPAI.Core.Models;
using MagicPAI.Core.Services;
using Moq;

namespace MagicPAI.Tests.Services;

/// <summary>
/// Tests for IContainerManager contract using a mock Docker client.
/// Real Docker integration tests require a running Docker daemon.
/// </summary>
public class DockerContainerManagerTests
{
    [Fact]
    public async Task MockContainerManager_SpawnAsync_ReturnsContainerId()
    {
        var mock = new Mock<IContainerManager>();
        mock.Setup(m => m.SpawnAsync(It.IsAny<ContainerConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContainerInfo("container-abc-123", null));

        var result = await mock.Object.SpawnAsync(new ContainerConfig(), CancellationToken.None);

        Assert.Equal("container-abc-123", result.ContainerId);
        Assert.Null(result.GuiUrl);
    }

    [Fact]
    public async Task MockContainerManager_SpawnAsync_WithGui_ReturnsGuiUrl()
    {
        var mock = new Mock<IContainerManager>();
        mock.Setup(m => m.SpawnAsync(It.IsAny<ContainerConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContainerInfo("container-gui-1", "http://127.0.0.1:7900/vnc.html?autoconnect=1&resize=scale"));

        var config = new ContainerConfig { EnableGui = true, GuiPort = 7900 };
        var result = await mock.Object.SpawnAsync(config, CancellationToken.None);

        Assert.Equal("container-gui-1", result.ContainerId);
        Assert.Equal("http://127.0.0.1:7900/vnc.html?autoconnect=1&resize=scale", result.GuiUrl);
    }

    [Fact]
    public async Task MockContainerManager_ExecAsync_ReturnsOutput()
    {
        var mock = new Mock<IContainerManager>();
        mock.Setup(m => m.ExecAsync("c1", "echo hello", "/workspace", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecResult(0, "hello\n", ""));

        var result = await mock.Object.ExecAsync("c1", "echo hello", "/workspace", CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hello", result.Output);
        Assert.Empty(result.Error);
    }

    [Fact]
    public async Task MockContainerManager_ExecAsync_ReturnsNonZeroExitCode()
    {
        var mock = new Mock<IContainerManager>();
        mock.Setup(m => m.ExecAsync("c1", "false", "/workspace", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecResult(1, "", "command failed"));

        var result = await mock.Object.ExecAsync("c1", "false", "/workspace", CancellationToken.None);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("command failed", result.Error);
    }

    [Fact]
    public async Task MockContainerManager_ExecStreamingAsync_InvokesCallback()
    {
        var chunks = new List<string>();
        var mock = new Mock<IContainerManager>();
        mock.Setup(m => m.ExecStreamingAsync("c1", "build", It.IsAny<Action<string>>(),
                It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, Action<string>, TimeSpan, CancellationToken>(
                (_, _, onOutput, _, _) =>
                {
                    onOutput("line 1\n");
                    onOutput("line 2\n");
                })
            .ReturnsAsync(new ExecResult(0, "line 1\nline 2\n", ""));

        var result = await mock.Object.ExecStreamingAsync("c1", "build",
            chunk => chunks.Add(chunk), TimeSpan.FromMinutes(5), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(2, chunks.Count);
    }

    [Fact]
    public async Task MockContainerManager_DestroyAsync_DoesNotThrow()
    {
        var mock = new Mock<IContainerManager>();
        mock.Setup(m => m.DestroyAsync("c1", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await mock.Object.DestroyAsync("c1", CancellationToken.None);
        mock.Verify(m => m.DestroyAsync("c1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MockContainerManager_IsRunningAsync_ReturnsTrue()
    {
        var mock = new Mock<IContainerManager>();
        mock.Setup(m => m.IsRunningAsync("c1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        Assert.True(await mock.Object.IsRunningAsync("c1", CancellationToken.None));
    }

    [Fact]
    public async Task MockContainerManager_IsRunningAsync_ReturnsFalseForUnknown()
    {
        var mock = new Mock<IContainerManager>();
        mock.Setup(m => m.IsRunningAsync("unknown", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        Assert.False(await mock.Object.IsRunningAsync("unknown", CancellationToken.None));
    }

    [Fact]
    public void MockContainerManager_GetGuiUrl_ReturnsNull_WhenNoGui()
    {
        var mock = new Mock<IContainerManager>();
        mock.Setup(m => m.GetGuiUrl("c1")).Returns((string?)null);

        Assert.Null(mock.Object.GetGuiUrl("c1"));
    }

    [Fact]
    public void ContainerConfig_HasCorrectDefaults()
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
    public void BuildCredentialBinds_Mounts_HostCredentialFiles_ReadOnly()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        Directory.CreateDirectory(Path.Combine(tempRoot, ".claude"));
        Directory.CreateDirectory(Path.Combine(tempRoot, ".codex"));
        File.WriteAllText(Path.Combine(tempRoot, ".claude.json"), "{}");
        File.WriteAllText(Path.Combine(tempRoot, ".claude", ".credentials.json"), "{}");
        File.WriteAllText(Path.Combine(tempRoot, ".codex", "auth.json"), "{}");
        File.WriteAllText(Path.Combine(tempRoot, ".codex", "cap_sid"), "sid");

        try
        {
            var binds = DockerContainerManager.BuildCredentialBinds(tempRoot);

            Assert.Contains(binds, bind => bind.EndsWith(":/tmp/magicpai-host-claude.json:ro"));
            Assert.Contains(binds, bind => bind.EndsWith(":/tmp/magicpai-host-claude-credentials.json:ro"));
            Assert.Contains(binds, bind => bind.EndsWith(":/tmp/magicpai-host-codex-auth.json:ro"));
            Assert.Contains(binds, bind => bind.EndsWith(":/tmp/magicpai-host-codex-cap-sid:ro"));
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }
}
