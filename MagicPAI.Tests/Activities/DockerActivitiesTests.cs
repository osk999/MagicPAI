using FluentAssertions;
using MagicPAI.Activities.Contracts;
using MagicPAI.Activities.Docker;
using MagicPAI.Core.Config;
using MagicPAI.Core.Models;
using MagicPAI.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Temporalio.Exceptions;
using Temporalio.Testing;

namespace MagicPAI.Tests.Activities;

/// <summary>
/// Unit tests for <see cref="DockerActivities"/> (Temporal activity group).
/// Uses <see cref="ActivityEnvironment"/> from the Temporal SDK for in-process
/// invocation + mocked <see cref="IContainerManager"/>. See temporal.md §15.3.
/// </summary>
[Trait("Category", "Unit")]
public class DockerActivitiesTests
{
    private static DockerActivities BuildSut(
        Mock<IContainerManager>? docker = null,
        Mock<ISessionStreamSink>? sink = null,
        MagicPaiConfig? config = null)
    {
        docker ??= new Mock<IContainerManager>(MockBehavior.Loose);
        sink ??= new Mock<ISessionStreamSink>(MockBehavior.Loose);
        config ??= new MagicPaiConfig { ExecutionBackend = "docker" };

        return new DockerActivities(
            docker: docker.Object,
            sink: sink.Object,
            config: config,
            log: NullLogger<DockerActivities>.Instance);
    }

    [Fact]
    public async Task SpawnAsync_ReturnsContainerId_WhenDockerAvailable()
    {
        // Arrange
        var docker = new Mock<IContainerManager>(MockBehavior.Strict);
        docker.Setup(d => d.SpawnAsync(It.IsAny<ContainerConfig>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new ContainerInfo("ctr-42", "http://localhost:6080/gui"));

        var sink = new Mock<ISessionStreamSink>(MockBehavior.Loose);
        var sut = BuildSut(docker: docker, sink: sink);

        var input = new SpawnContainerInput(
            SessionId: "session-1",
            Image: "magicpai-env:latest",
            WorkspacePath: "/tmp/work");

        // Act
        var env = new ActivityEnvironment();
        var output = await env.RunAsync(() => sut.SpawnAsync(input));

        // Assert
        output.ContainerId.Should().Be("ctr-42");
        output.GuiUrl.Should().Be("http://localhost:6080/gui");

        docker.Verify(d => d.SpawnAsync(
            It.Is<ContainerConfig>(c => c.Image == "magicpai-env:latest"
                                     && c.WorkspacePath == "/tmp/work"),
            It.IsAny<CancellationToken>()), Times.Once);

        sink.Verify(s => s.EmitStructuredAsync(
            "session-1",
            "ContainerSpawned",
            It.IsAny<object>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SpawnAsync_ThrowsConfigError_WhenBackendIsNotDocker()
    {
        // Arrange
        var cfg = new MagicPaiConfig
        {
            ExecutionBackend = "kubernetes",
            UseDocker = false,
            KubernetesNamespace = "magicpai",
            // Must not fail Validate(); invariant under test is the activity body.
        };
        var sut = BuildSut(config: cfg);
        var input = new SpawnContainerInput(SessionId: "session-1");

        // Act
        var env = new ActivityEnvironment();
        Func<Task> act = async () =>
            await env.RunAsync(() => sut.SpawnAsync(input));

        // Assert
        await act.Should()
            .ThrowAsync<ApplicationFailureException>()
            .Where(e => e.ErrorType == "ConfigError");
    }

    [Fact]
    public async Task ExecAsync_ThrowsApplicationFailure_OnException()
    {
        // Arrange
        var docker = new Mock<IContainerManager>(MockBehavior.Strict);
        docker.Setup(d => d.ExecAsync(
                  It.IsAny<string>(),
                  It.IsAny<string>(),
                  It.IsAny<string>(),
                  It.IsAny<CancellationToken>()))
              .ThrowsAsync(new InvalidOperationException("docker daemon unreachable"));

        var sut = BuildSut(docker: docker);
        var input = new ExecInput(ContainerId: "ctr-1", Command: "echo hi");

        // Act
        var env = new ActivityEnvironment();
        Func<Task> act = async () =>
            await env.RunAsync(() => sut.ExecAsync(input));

        // Assert
        await act.Should()
            .ThrowAsync<ApplicationFailureException>()
            .Where(e => e.ErrorType == "ExecError");
    }

    [Fact]
    public async Task ExecAsync_TruncatesOutputPast64KB()
    {
        // Arrange — returns an oversized output
        var oversized = new string('x', 70_000);
        var docker = new Mock<IContainerManager>(MockBehavior.Strict);
        docker.Setup(d => d.ExecAsync(
                  It.IsAny<string>(),
                  It.IsAny<string>(),
                  It.IsAny<string>(),
                  It.IsAny<CancellationToken>()))
              .ReturnsAsync(new ExecResult(0, oversized, ""));

        var sut = BuildSut(docker: docker);
        var input = new ExecInput(ContainerId: "ctr-1", Command: "cat bigfile");

        // Act
        var env = new ActivityEnvironment();
        var output = await env.RunAsync(() => sut.ExecAsync(input));

        // Assert — cap at 64 KB + marker
        output.Output.Length.Should().BeLessThan(70_000);
        output.Output.Should().EndWith("...[truncated]...");
        output.ExitCode.Should().Be(0);
    }

    [Fact]
    public async Task DestroyAsync_CallsDockerDestroy()
    {
        // Arrange
        var docker = new Mock<IContainerManager>(MockBehavior.Strict);
        docker.Setup(d => d.DestroyAsync("ctr-99", It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);

        var sut = BuildSut(docker: docker);
        var input = new DestroyInput(ContainerId: "ctr-99");

        // Act
        var env = new ActivityEnvironment();
        await env.RunAsync(() => sut.DestroyAsync(input));

        // Assert
        docker.Verify(d => d.DestroyAsync("ctr-99", It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
