using MagicPAI.Core.Models;
using MagicPAI.Core.Services;
using Moq;

namespace MagicPAI.Tests.Activities;

/// <summary>
/// Tests for the RunVerification and Repair activity logic.
/// We test the VerificationPipeline integration and repair prompt generation.
/// </summary>
public class VerificationActivityTests
{
    [Fact]
    public async Task Verification_AllPassed_Routes_To_Passed()
    {
        var mockGate = new Mock<IVerificationGate>();
        mockGate.Setup(g => g.Name).Returns("compile");
        mockGate.Setup(g => g.IsBlocking).Returns(true);
        mockGate.Setup(g => g.CanVerifyAsync(It.IsAny<IContainerManager>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        mockGate.Setup(g => g.VerifyAsync(It.IsAny<IContainerManager>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GateResult("compile", true, "Build succeeded", [], TimeSpan.FromSeconds(2)));

        var pipeline = new VerificationPipeline([mockGate.Object]);
        var mockContainer = new Mock<IContainerManager>();

        var result = await pipeline.RunAsync(
            mockContainer.Object, "c1", "/workspace",
            ["compile"], null, CancellationToken.None);

        Assert.True(result.AllPassed);
        var failedGates = result.Gates.Where(g => !g.Passed).Select(g => g.Name).ToArray();
        Assert.Empty(failedGates);

        // Activity would route to "Passed" outcome
        var outcome = result.IsInconclusive ? "Inconclusive"
            : result.AllPassed ? "Passed" : "Failed";
        Assert.Equal("Passed", outcome);
    }

    [Fact]
    public async Task Verification_BlockingFailure_Routes_To_Failed()
    {
        var compileGate = new Mock<IVerificationGate>();
        compileGate.Setup(g => g.Name).Returns("compile");
        compileGate.Setup(g => g.IsBlocking).Returns(true);
        compileGate.Setup(g => g.CanVerifyAsync(It.IsAny<IContainerManager>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        compileGate.Setup(g => g.VerifyAsync(It.IsAny<IContainerManager>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GateResult("compile", false, "Build failed",
                ["error CS1002: ; expected"], TimeSpan.FromSeconds(3)));

        var pipeline = new VerificationPipeline([compileGate.Object]);
        var mockContainer = new Mock<IContainerManager>();

        var result = await pipeline.RunAsync(
            mockContainer.Object, "c1", "/workspace",
            ["compile"], null, CancellationToken.None);

        Assert.False(result.AllPassed);
        var failedGates = result.Gates.Where(g => !g.Passed).Select(g => g.Name).ToArray();
        Assert.Contains("compile", failedGates);

        var outcome = result.IsInconclusive ? "Inconclusive"
            : result.AllPassed ? "Passed" : "Failed";
        Assert.Equal("Failed", outcome);
    }

    [Fact]
    public async Task Verification_NonBlockingFailure_Still_Continues()
    {
        var lintGate = new Mock<IVerificationGate>();
        lintGate.Setup(g => g.Name).Returns("lint");
        lintGate.Setup(g => g.IsBlocking).Returns(false);
        lintGate.Setup(g => g.CanVerifyAsync(It.IsAny<IContainerManager>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        lintGate.Setup(g => g.VerifyAsync(It.IsAny<IContainerManager>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GateResult("lint", false, "Style issues found",
                ["trailing whitespace"], TimeSpan.FromSeconds(1)));

        var compileGate = new Mock<IVerificationGate>();
        compileGate.Setup(g => g.Name).Returns("compile");
        compileGate.Setup(g => g.IsBlocking).Returns(true);
        compileGate.Setup(g => g.CanVerifyAsync(It.IsAny<IContainerManager>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        compileGate.Setup(g => g.VerifyAsync(It.IsAny<IContainerManager>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GateResult("compile", true, "Build OK", [], TimeSpan.FromSeconds(2)));

        var pipeline = new VerificationPipeline([lintGate.Object, compileGate.Object]);
        var mockContainer = new Mock<IContainerManager>();

        var result = await pipeline.RunAsync(
            mockContainer.Object, "c1", "/workspace",
            ["lint", "compile"], null, CancellationToken.None);

        // Both gates ran
        Assert.Equal(2, result.Gates.Count);
        // AllPassed is true because lint is non-blocking
        Assert.True(result.AllPassed);
    }

    [Fact]
    public async Task Verification_EmptyGateList_Returns_AllPassed()
    {
        var pipeline = new VerificationPipeline([]);
        var mockContainer = new Mock<IContainerManager>();

        var result = await pipeline.RunAsync(
            mockContainer.Object, "c1", "/workspace",
            [], null, CancellationToken.None);

        Assert.True(result.AllPassed);
        Assert.Empty(result.Gates);
    }

    [Fact]
    public void GateResult_Records_Issues()
    {
        var result = new GateResult("test", false, "3 tests failed",
            ["TestA failed", "TestB failed", "TestC failed"], TimeSpan.FromSeconds(10));

        Assert.False(result.Passed);
        Assert.Equal(3, result.Issues.Length);
        Assert.Equal("test", result.Name);
    }

    [Fact]
    public void PipelineResult_IsInconclusive_WhenNoGatesRan()
    {
        var result = new PipelineResult
        {
            Gates = [],
            AllPassed = true
        };

        // No gates = inconclusive
        Assert.True(result.IsInconclusive);
    }
}
