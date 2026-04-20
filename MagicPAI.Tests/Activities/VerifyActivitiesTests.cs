using FluentAssertions;
using MagicPAI.Activities.Contracts;
using MagicPAI.Activities.Verification;
using MagicPAI.Core.Config;
using MagicPAI.Core.Models;
using MagicPAI.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Temporalio.Testing;

namespace MagicPAI.Tests.Activities;

/// <summary>
/// Unit tests for <see cref="VerifyActivities"/> — Temporal verification + repair
/// activities. <c>RunGatesAsync</c> is exercised with a mocked
/// <see cref="IVerificationGate"/>; <c>GenerateRepairPromptAsync</c> is pure CPU.
/// See temporal.md §I.3.
/// </summary>
[Trait("Category", "Unit")]
public class VerifyActivitiesTests
{
    private static VerifyActivities BuildSut(
        IEnumerable<IVerificationGate>? gates = null,
        Mock<IContainerManager>? docker = null,
        Mock<ISessionStreamSink>? sink = null,
        MagicPaiConfig? config = null)
    {
        docker ??= new Mock<IContainerManager>(MockBehavior.Loose);
        sink ??= new Mock<ISessionStreamSink>(MockBehavior.Loose);
        config ??= new MagicPaiConfig { ExecutionBackend = "docker" };
        gates ??= Array.Empty<IVerificationGate>();

        var pipeline = new VerificationPipeline(gates);

        return new VerifyActivities(
            docker: docker.Object,
            pipeline: pipeline,
            config: config,
            sink: sink.Object,
            log: NullLogger<VerifyActivities>.Instance);
    }

    // ── RunGatesAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task RunGatesAsync_ReturnsAllPassed_WhenGatePasses()
    {
        // Arrange — one passing compile gate.
        var compileGate = new Mock<IVerificationGate>();
        compileGate.SetupGet(g => g.Name).Returns("compile");
        compileGate.SetupGet(g => g.IsBlocking).Returns(true);
        compileGate.Setup(g => g.CanVerifyAsync(It.IsAny<IContainerManager>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        compileGate.Setup(g => g.VerifyAsync(It.IsAny<IContainerManager>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GateResult("compile", true, "OK", Array.Empty<string>(), TimeSpan.FromSeconds(1)));

        var sink = new Mock<ISessionStreamSink>(MockBehavior.Strict);
        sink.Setup(s => s.EmitStructuredAsync(
                "sess-1", "VerificationComplete",
                It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = BuildSut(gates: new[] { compileGate.Object }, sink: sink);
        var input = new VerifyInput(
            ContainerId: "ctr-55",
            WorkingDirectory: "/workspace",
            EnabledGates: new[] { "compile" },
            WorkerOutput: "build log",
            SessionId: "sess-1");

        // Act
        var env = new ActivityEnvironment();
        var output = await env.RunAsync(() => sut.RunGatesAsync(input));

        // Assert
        output.AllPassed.Should().BeTrue();
        output.FailedGates.Should().BeEmpty();
        output.GateResultsJson.Should().Contain("compile");
        sink.Verify(s => s.EmitStructuredAsync(
            "sess-1", "VerificationComplete",
            It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunGatesAsync_ReportsFailedGates_WhenBlockingGateFails()
    {
        var compileGate = new Mock<IVerificationGate>();
        compileGate.SetupGet(g => g.Name).Returns("compile");
        compileGate.SetupGet(g => g.IsBlocking).Returns(true);
        compileGate.Setup(g => g.CanVerifyAsync(It.IsAny<IContainerManager>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        compileGate.Setup(g => g.VerifyAsync(It.IsAny<IContainerManager>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GateResult("compile", false, "Build failed",
                new[] { "error CS1002" }, TimeSpan.FromSeconds(2)));

        var sut = BuildSut(gates: new[] { compileGate.Object });
        var input = new VerifyInput(
            ContainerId: "ctr-56",
            WorkingDirectory: "/workspace",
            EnabledGates: new[] { "compile" },
            WorkerOutput: "build log",
            SessionId: null);  // no sink emit

        var env = new ActivityEnvironment();
        var output = await env.RunAsync(() => sut.RunGatesAsync(input));

        output.AllPassed.Should().BeFalse();
        output.FailedGates.Should().ContainSingle().Which.Should().Be("compile");
        output.GateResultsJson.Should().Contain("Build failed");
    }

    // ── GenerateRepairPromptAsync ────────────────────────────────────────

    [Fact]
    public async Task GenerateRepairPromptAsync_ReturnsPrompt_WhenUnderAttemptBudget()
    {
        var sut = BuildSut();
        var input = new RepairInput(
            ContainerId: "ctr-55",
            FailedGates: new[] { "compile", "test" },
            OriginalPrompt: "Add a /health endpoint",
            GateResultsJson: """[{"Name":"compile","Passed":false}]""",
            AttemptNumber: 1,
            MaxAttempts: 3);

        var env = new ActivityEnvironment();
        var output = await env.RunAsync(() => sut.GenerateRepairPromptAsync(input));

        output.ShouldAttemptRepair.Should().BeTrue();
        output.RepairPrompt.Should().Contain("compile, test");
        output.RepairPrompt.Should().Contain("Add a /health endpoint");
        output.RepairPrompt.Should().Contain("Attempt 1 of 3");
    }

    [Fact]
    public async Task GenerateRepairPromptAsync_Refuses_WhenAttemptBudgetExhausted()
    {
        var sut = BuildSut();
        var input = new RepairInput(
            ContainerId: "ctr-55",
            FailedGates: new[] { "compile" },
            OriginalPrompt: "whatever",
            GateResultsJson: "[]",
            AttemptNumber: 4,
            MaxAttempts: 3);

        var env = new ActivityEnvironment();
        var output = await env.RunAsync(() => sut.GenerateRepairPromptAsync(input));

        output.ShouldAttemptRepair.Should().BeFalse();
        output.RepairPrompt.Should().BeEmpty();
    }
}
