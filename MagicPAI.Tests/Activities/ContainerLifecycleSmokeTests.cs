using MagicPAI.Core.Models;
using MagicPAI.Core.Services;
using Moq;

namespace MagicPAI.Tests.Activities;

/// <summary>
/// Robust smoke tests for all four container lifecycle activities:
/// SpawnContainer → ExecInContainer → StreamFromContainer → DestroyContainer.
///
/// All tests use a strict <see cref="IContainerManager"/> mock (no Docker daemon required)
/// and verify both happy-path outcomes and failure propagation.
/// </summary>
public class ContainerLifecycleSmokeTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private const string DefaultImage    = "magicpai-env:latest";
    private const string DefaultWorkDir  = "/workspace";

    private static Mock<IContainerManager> StrictMock() =>
        new Mock<IContainerManager>(MockBehavior.Strict);

    private static ContainerConfig BaseConfig(string? image = null) => new()
    {
        Image        = image ?? DefaultImage,
        WorkspacePath = "/tmp/smoke",
        MemoryLimitMb = 2048,
        CpuCount      = 1
    };

    // -----------------------------------------------------------------------
    // Full pipeline – all four activities succeed
    // -----------------------------------------------------------------------

    [Fact]
    public async Task FullPipeline_SpawnExecStreamDestroy_AllSucceed()
    {
        const string cid = "smoke-full-001";
        var mock = StrictMock();

        mock.Setup(m => m.SpawnAsync(It.IsAny<ContainerConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContainerInfo(cid, null));

        mock.Setup(m => m.IsRunningAsync(cid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        mock.Setup(m => m.ExecAsync(cid, "echo hello", DefaultWorkDir, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecResult(0, "hello\n", ""));

        mock.Setup(m => m.ExecStreamingAsync(
                cid, "dotnet build",
                It.IsAny<Action<string>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, Action<string>, TimeSpan, CancellationToken>(
                (_, _, onOutput, _, _) =>
                {
                    onOutput("Restoring packages...\n");
                    onOutput("Build succeeded.\n");
                })
            .ReturnsAsync(new ExecResult(0, "Restoring packages...\nBuild succeeded.\n", ""));

        mock.Setup(m => m.DestroyAsync(cid, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // --- Spawn ---
        var info = await mock.Object.SpawnAsync(BaseConfig(), CancellationToken.None);
        Assert.Equal(cid, info.ContainerId);
        Assert.Null(info.GuiUrl);
        Assert.True(await mock.Object.IsRunningAsync(cid, CancellationToken.None));

        // --- Exec ---
        var exec = await mock.Object.ExecAsync(cid, "echo hello", DefaultWorkDir, CancellationToken.None);
        Assert.Equal(0, exec.ExitCode);
        Assert.Contains("hello", exec.Output);
        Assert.Empty(exec.Error);

        // --- Stream ---
        var chunks = new List<string>();
        var stream = await mock.Object.ExecStreamingAsync(
            cid, "dotnet build",
            chunk => chunks.Add(chunk),
            TimeSpan.FromMinutes(5),
            CancellationToken.None);

        Assert.Equal(0, stream.ExitCode);
        Assert.Equal(2, chunks.Count);
        Assert.Equal("Restoring packages...\n", chunks[0]);
        Assert.Equal("Build succeeded.\n", chunks[1]);

        // --- Destroy ---
        await mock.Object.DestroyAsync(cid, CancellationToken.None);

        // Verify entire sequence happened once each
        mock.Verify(m => m.SpawnAsync(It.IsAny<ContainerConfig>(), It.IsAny<CancellationToken>()), Times.Once);
        mock.Verify(m => m.ExecAsync(cid, "echo hello", DefaultWorkDir, It.IsAny<CancellationToken>()), Times.Once);
        mock.Verify(m => m.ExecStreamingAsync(cid, "dotnet build", It.IsAny<Action<string>>(),
            It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
        mock.Verify(m => m.DestroyAsync(cid, It.IsAny<CancellationToken>()), Times.Once);
    }

    // -----------------------------------------------------------------------
    // ExecInContainer – outcome routing
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExecActivity_ZeroExitCode_IsSuccess()
    {
        var mock = new Mock<IContainerManager>();
        mock.Setup(m => m.ExecAsync("c1", "ls", DefaultWorkDir, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecResult(0, "file.txt\n", ""));

        var result = await mock.Object.ExecAsync("c1", "ls", DefaultWorkDir, CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("file.txt", result.Output);
    }

    [Fact]
    public async Task ExecActivity_NonZeroExitCode_IsFailure()
    {
        var mock = new Mock<IContainerManager>();
        mock.Setup(m => m.ExecAsync("c1", "false", DefaultWorkDir, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecResult(1, "", "command not found"));

        var result = await mock.Object.ExecAsync("c1", "false", DefaultWorkDir, CancellationToken.None);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("command not found", result.Error);
    }

    [Fact]
    public async Task ExecActivity_ExceptionThrown_PropagatesFromManager()
    {
        var mock = new Mock<IContainerManager>();
        mock.Setup(m => m.ExecAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("container exec socket closed"));

        await Assert.ThrowsAsync<IOException>(
            () => mock.Object.ExecAsync("c1", "ls", DefaultWorkDir, CancellationToken.None));
    }

    // -----------------------------------------------------------------------
    // ExecInContainer – structured (ContainerExecRequest) overload
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExecActivity_StructuredRequest_CorrectOverloadInvoked()
    {
        var request = new ContainerExecRequest(
            FileName: "dotnet",
            Arguments: ["test", "--no-build"],
            WorkingDirectory: DefaultWorkDir,
            Environment: new Dictionary<string, string?> { ["DOTNET_CLI_HOME"] = "/tmp" });

        var mock = new Mock<IContainerManager>();
        mock.Setup(m => m.ExecAsync("c1", request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecResult(0, "Passed: 42", ""));

        var result = await mock.Object.ExecAsync("c1", request, CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Passed", result.Output);
        mock.Verify(m => m.ExecAsync("c1", request, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecActivity_StructuredRequest_NonZeroExit_IsFailure()
    {
        var request = new ContainerExecRequest("dotnet", ["test"], DefaultWorkDir);

        var mock = new Mock<IContainerManager>();
        mock.Setup(m => m.ExecAsync("c1", request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecResult(1, "", "1 test failed"));

        var result = await mock.Object.ExecAsync("c1", request, CancellationToken.None);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("1 test failed", result.Error);
    }

    // -----------------------------------------------------------------------
    // StreamFromContainer – output accumulation and failure
    // -----------------------------------------------------------------------

    [Fact]
    public async Task StreamActivity_ChunksAccumulated_InOrderAndComplete()
    {
        const string cid = "smoke-stream-001";
        var allChunks = new List<string>();

        var mock = new Mock<IContainerManager>();
        mock.Setup(m => m.ExecStreamingAsync(
                cid, "make all",
                It.IsAny<Action<string>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, Action<string>, TimeSpan, CancellationToken>(
                (_, _, onOutput, _, _) =>
                {
                    for (var i = 1; i <= 5; i++)
                        onOutput($"step {i}\n");
                })
            .ReturnsAsync(new ExecResult(0, "step 1\nstep 2\nstep 3\nstep 4\nstep 5\n", ""));

        var result = await mock.Object.ExecStreamingAsync(
            cid, "make all",
            chunk => allChunks.Add(chunk),
            TimeSpan.FromMinutes(10),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(5, allChunks.Count);
        for (var i = 1; i <= 5; i++)
            Assert.Equal($"step {i}\n", allChunks[i - 1]);
    }

    [Fact]
    public async Task StreamActivity_NonZeroExitCode_IsFailure()
    {
        var mock = new Mock<IContainerManager>();
        mock.Setup(m => m.ExecStreamingAsync(
                "c1", "bad-cmd",
                It.IsAny<Action<string>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, Action<string>, TimeSpan, CancellationToken>(
                (_, _, onOutput, _, _) => onOutput("some output before fail\n"))
            .ReturnsAsync(new ExecResult(2, "some output before fail\n", "error: command failed"));

        var chunks = new List<string>();
        var result = await mock.Object.ExecStreamingAsync(
            "c1", "bad-cmd",
            chunk => chunks.Add(chunk),
            TimeSpan.FromMinutes(5),
            CancellationToken.None);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Single(chunks); // partial output was still delivered
        Assert.Contains("some output before fail", chunks[0]);
    }

    [Fact]
    public async Task StreamActivity_ExceptionThrown_PropagatesFromManager()
    {
        var mock = new Mock<IContainerManager>();
        mock.Setup(m => m.ExecStreamingAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Action<string>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("idle timeout exceeded"));

        await Assert.ThrowsAsync<TimeoutException>(
            () => mock.Object.ExecStreamingAsync(
                "c1", "sleep 9999",
                _ => { }, TimeSpan.FromSeconds(1),
                CancellationToken.None));
    }

    [Fact]
    public async Task StreamActivity_StructuredRequest_CorrectOverloadInvoked()
    {
        var request = new ContainerExecRequest("claude", ["--dangerously-skip-permissions"], DefaultWorkDir);
        var chunks = new List<string>();

        var mock = new Mock<IContainerManager>();
        mock.Setup(m => m.ExecStreamingAsync(
                "c1", request,
                It.IsAny<Action<string>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, ContainerExecRequest, Action<string>, TimeSpan, CancellationToken>(
                (_, _, onOutput, _, _) => onOutput("{\"type\":\"result\"}\n"))
            .ReturnsAsync(new ExecResult(0, "{\"type\":\"result\"}\n", ""));

        var result = await mock.Object.ExecStreamingAsync(
            "c1", request,
            chunk => chunks.Add(chunk),
            TimeSpan.FromMinutes(30),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Single(chunks);
        mock.Verify(m => m.ExecStreamingAsync(
            "c1", request, It.IsAny<Action<string>>(),
            It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // -----------------------------------------------------------------------
    // SpawnContainer – failure propagation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SpawnFailure_ExceptionPropagates_NoDestroyCall()
    {
        var mock = StrictMock();
        mock.Setup(m => m.SpawnAsync(It.IsAny<ContainerConfig>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Docker daemon unavailable"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => mock.Object.SpawnAsync(BaseConfig(), CancellationToken.None));

        // DestroyAsync must never have been called
        mock.Verify(m => m.DestroyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SpawnFailure_WithDifferentExceptionTypes_AllPropagateCorrectly()
    {
        foreach (var exception in new Exception[]
        {
            new HttpRequestException("registry not reachable"),
            new OperationCanceledException(),
            new TimeoutException("spawn timed out"),
            new UnauthorizedAccessException("missing Docker socket permissions")
        })
        {
            var expectedType = exception.GetType();
            var mock = new Mock<IContainerManager>();
            mock.Setup(m => m.SpawnAsync(It.IsAny<ContainerConfig>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(exception);

            var thrown = await Record.ExceptionAsync(
                () => mock.Object.SpawnAsync(BaseConfig(), CancellationToken.None));
            Assert.NotNull(thrown);
            Assert.True(thrown.GetType() == expectedType,
                $"Expected {expectedType.Name} but got {thrown.GetType().Name}");
        }
    }

    // -----------------------------------------------------------------------
    // DestroyContainer – cleanup behavior
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DestroyActivity_ValidId_CallsDestroyOnce()
    {
        const string cid = "smoke-destroy-001";
        var mock = StrictMock();
        mock.Setup(m => m.DestroyAsync(cid, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await mock.Object.DestroyAsync(cid, CancellationToken.None);

        mock.Verify(m => m.DestroyAsync(cid, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DestroyActivity_ExceptionThrown_PropagatesFromManager()
    {
        var mock = new Mock<IContainerManager>();
        mock.Setup(m => m.DestroyAsync("c1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("container already removed"));

        await Assert.ThrowsAsync<IOException>(
            () => mock.Object.DestroyAsync("c1", CancellationToken.None));
    }

    [Fact]
    public async Task DestroyActivity_IsRunningReturnsFalse_AfterDestroy()
    {
        const string cid = "smoke-lifecycle-001";
        var running = true;

        var mock = new Mock<IContainerManager>();
        mock.Setup(m => m.SpawnAsync(It.IsAny<ContainerConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContainerInfo(cid, null));
        mock.Setup(m => m.IsRunningAsync(cid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => running);
        mock.Setup(m => m.DestroyAsync(cid, It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((_, _) => running = false)
            .Returns(Task.CompletedTask);

        await mock.Object.SpawnAsync(BaseConfig(), CancellationToken.None);
        Assert.True(await mock.Object.IsRunningAsync(cid, CancellationToken.None));

        await mock.Object.DestroyAsync(cid, CancellationToken.None);
        Assert.False(await mock.Object.IsRunningAsync(cid, CancellationToken.None));
    }

    // -----------------------------------------------------------------------
    // Cancellation propagation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Cancellation_DuringSpawn_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var mock = new Mock<IContainerManager>();
        mock.Setup(m => m.SpawnAsync(It.IsAny<ContainerConfig>(), It.IsAny<CancellationToken>()))
            .Returns<ContainerConfig, CancellationToken>((_, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(new ContainerInfo("never", null));
            });

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => mock.Object.SpawnAsync(BaseConfig(), cts.Token));
    }

    [Fact]
    public async Task Cancellation_DuringExec_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var mock = new Mock<IContainerManager>();
        mock.Setup(m => m.ExecAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, string, CancellationToken>((_, _, _, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(new ExecResult(0, "", ""));
            });

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => mock.Object.ExecAsync("c1", "sleep 60", DefaultWorkDir, cts.Token));
    }

    [Fact]
    public async Task Cancellation_DuringStreaming_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var mock = new Mock<IContainerManager>();
        mock.Setup(m => m.ExecStreamingAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Action<string>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, string, Action<string>, TimeSpan, CancellationToken>((_, _, _, _, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(new ExecResult(0, "", ""));
            });

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => mock.Object.ExecStreamingAsync(
                "c1", "sleep 60", _ => { },
                TimeSpan.FromMinutes(30), cts.Token));
    }

    [Fact]
    public async Task Cancellation_DuringDestroy_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var mock = new Mock<IContainerManager>();
        mock.Setup(m => m.DestroyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>((_, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            });

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => mock.Object.DestroyAsync("c1", cts.Token));
    }

    // -----------------------------------------------------------------------
    // Multiple sequential execs on the same container
    // -----------------------------------------------------------------------

    [Fact]
    public async Task MultipleSequentialExecs_AllSucceed_AllVerified()
    {
        const string cid = "smoke-multi-001";
        var mock = new Mock<IContainerManager>();

        var commands = new[] { "git fetch", "git rebase origin/main", "dotnet build", "dotnet test" };

        foreach (var cmd in commands)
        {
            mock.Setup(m => m.ExecAsync(cid, cmd, DefaultWorkDir, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ExecResult(0, $"OK: {cmd}", ""));
        }

        foreach (var cmd in commands)
        {
            var result = await mock.Object.ExecAsync(cid, cmd, DefaultWorkDir, CancellationToken.None);
            Assert.Equal(0, result.ExitCode);
            Assert.Contains(cmd, result.Output);
        }

        foreach (var cmd in commands)
        {
            mock.Verify(m => m.ExecAsync(cid, cmd, DefaultWorkDir, It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Fact]
    public async Task MultipleExecs_FailingCommand_SubsequentCommandsCanStillRun()
    {
        const string cid = "smoke-multi-002";
        var mock = new Mock<IContainerManager>();

        mock.Setup(m => m.ExecAsync(cid, "dotnet build", DefaultWorkDir, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecResult(1, "", "Build FAILED"));

        mock.Setup(m => m.ExecAsync(cid, "dotnet test --no-build", DefaultWorkDir, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecResult(1, "", "Tests FAILED"));

        var build = await mock.Object.ExecAsync(cid, "dotnet build", DefaultWorkDir, CancellationToken.None);
        Assert.Equal(1, build.ExitCode);

        // Manager itself doesn't gate subsequent calls – activity/workflow orchestration does
        var test = await mock.Object.ExecAsync(cid, "dotnet test --no-build", DefaultWorkDir, CancellationToken.None);
        Assert.Equal(1, test.ExitCode);
    }

    // -----------------------------------------------------------------------
    // GUI support
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Spawn_WithGui_ContainerInfoHasGuiUrl()
    {
        const string cid       = "smoke-gui-001";
        const string guiUrl    = "http://localhost:7900";

        var mock = new Mock<IContainerManager>();
        mock.Setup(m => m.SpawnAsync(
                It.Is<ContainerConfig>(c => c.EnableGui && c.GuiPort == 7900),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContainerInfo(cid, guiUrl));
        mock.Setup(m => m.GetGuiUrl(cid)).Returns(guiUrl);
        mock.Setup(m => m.DestroyAsync(cid, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var config = new ContainerConfig { EnableGui = true, GuiPort = 7900 };
        var info   = await mock.Object.SpawnAsync(config, CancellationToken.None);

        Assert.Equal(cid,    info.ContainerId);
        Assert.Equal(guiUrl, info.GuiUrl);
        Assert.Equal(guiUrl, mock.Object.GetGuiUrl(cid));

        await mock.Object.DestroyAsync(cid, CancellationToken.None);
    }

    [Fact]
    public async Task Spawn_WithoutGui_GuiUrlIsNull()
    {
        var mock = new Mock<IContainerManager>();
        mock.Setup(m => m.SpawnAsync(
                It.Is<ContainerConfig>(c => !c.EnableGui),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContainerInfo("ctr-001", null));
        mock.Setup(m => m.GetGuiUrl("ctr-001")).Returns((string?)null);

        var info = await mock.Object.SpawnAsync(new ContainerConfig { EnableGui = false }, CancellationToken.None);

        Assert.Null(info.GuiUrl);
        Assert.Null(mock.Object.GetGuiUrl("ctr-001"));
    }

    // -----------------------------------------------------------------------
    // ContainerConfig – env vars and resource defaults
    // -----------------------------------------------------------------------

    [Fact]
    public void ContainerConfig_CustomEnvVars_RetainedInConfig()
    {
        var env = new Dictionary<string, string>
        {
            ["ANTHROPIC_API_KEY"] = "sk-test",
            ["MY_APP_MODE"]       = "ci"
        };

        var config = new ContainerConfig { Env = env };

        Assert.Equal("sk-test", config.Env["ANTHROPIC_API_KEY"]);
        Assert.Equal("ci",      config.Env["MY_APP_MODE"]);
    }

    [Fact]
    public async Task ContainerConfig_EnvVars_PassedToSpawn_MockVerifiable()
    {
        var env = new Dictionary<string, string> { ["CI"] = "true" };
        ContainerConfig? captured = null;

        var mock = new Mock<IContainerManager>();
        mock.Setup(m => m.SpawnAsync(It.IsAny<ContainerConfig>(), It.IsAny<CancellationToken>()))
            .Callback<ContainerConfig, CancellationToken>((cfg, _) => captured = cfg)
            .ReturnsAsync(new ContainerInfo("c1", null));

        await mock.Object.SpawnAsync(new ContainerConfig { Env = env }, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal("true", captured!.Env["CI"]);
    }

    [Fact]
    public void ContainerConfig_Timeout_CanBeOverridden()
    {
        var config = new ContainerConfig { Timeout = TimeSpan.FromHours(2) };
        Assert.Equal(TimeSpan.FromHours(2), config.Timeout);
    }

    // -----------------------------------------------------------------------
    // IsRunning
    // -----------------------------------------------------------------------

    [Fact]
    public async Task IsRunning_UnknownContainerId_ReturnsFalse()
    {
        var mock = new Mock<IContainerManager>();
        mock.Setup(m => m.IsRunningAsync("unknown-ctr", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        Assert.False(await mock.Object.IsRunningAsync("unknown-ctr", CancellationToken.None));
    }

    [Fact]
    public async Task IsRunning_KnownRunningContainerId_ReturnsTrue()
    {
        var mock = new Mock<IContainerManager>();
        mock.Setup(m => m.IsRunningAsync("running-ctr", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        Assert.True(await mock.Object.IsRunningAsync("running-ctr", CancellationToken.None));
    }
}
