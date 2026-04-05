using MagicPAI.Core.Models;
using MagicPAI.Core.Services;
using Moq;

namespace MagicPAI.Tests.Services;

public class VerificationPipelineTests
{
    private readonly Mock<IContainerManager> _containerMock = new();

    private static Mock<IVerificationGate> CreateGate(
        string name, bool isBlocking, bool canVerify, bool passes,
        string output = "", string[]? issues = null)
    {
        var gate = new Mock<IVerificationGate>();
        gate.Setup(g => g.Name).Returns(name);
        gate.Setup(g => g.IsBlocking).Returns(isBlocking);
        gate.Setup(g => g.CanVerifyAsync(
                It.IsAny<IContainerManager>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(canVerify);
        gate.Setup(g => g.VerifyAsync(
                It.IsAny<IContainerManager>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GateResult(name, passes, output, issues ?? [], TimeSpan.FromMilliseconds(100)));
        return gate;
    }

    [Fact]
    public async Task RunAsync_All_Gates_Pass()
    {
        var gates = new[]
        {
            CreateGate("compile", true, true, true).Object,
            CreateGate("test", true, true, true).Object,
            CreateGate("lint", false, true, true).Object
        };

        var pipeline = new VerificationPipeline(gates);
        var result = await pipeline.RunAsync(
            _containerMock.Object, "container-1", "/workspace",
            ["compile", "test", "lint"], null, CancellationToken.None);

        Assert.True(result.AllPassed);
        Assert.Equal(3, result.Gates.Count);
        Assert.All(result.Gates, g => Assert.True(g.Passed));
    }

    [Fact]
    public async Task RunAsync_Blocking_Gate_Failure_Stops_Early()
    {
        var compileMock = CreateGate("compile", isBlocking: true, canVerify: true, passes: false, output: "Build failed");
        var testMock = CreateGate("test", isBlocking: true, canVerify: true, passes: true);

        var pipeline = new VerificationPipeline([compileMock.Object, testMock.Object]);
        var result = await pipeline.RunAsync(
            _containerMock.Object, "container-1", "/workspace",
            ["compile", "test"], null, CancellationToken.None);

        Assert.False(result.AllPassed);
        Assert.Single(result.Gates);
        Assert.Equal("compile", result.Gates[0].Name);
        Assert.False(result.Gates[0].Passed);

        // Test gate should NOT have been called
        testMock.Verify(g => g.VerifyAsync(
            It.IsAny<IContainerManager>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_NonBlocking_Gate_Failure_Continues()
    {
        var lintMock = CreateGate("lint", isBlocking: false, canVerify: true, passes: false, output: "Lint warnings");
        var testMock = CreateGate("test", isBlocking: true, canVerify: true, passes: true);

        var pipeline = new VerificationPipeline([lintMock.Object, testMock.Object]);
        var result = await pipeline.RunAsync(
            _containerMock.Object, "container-1", "/workspace",
            ["lint", "test"], null, CancellationToken.None);

        // AllPassed should be true because the failed gate is non-blocking
        Assert.True(result.AllPassed);
        Assert.Equal(2, result.Gates.Count);
    }

    [Fact]
    public async Task RunAsync_Skips_Gate_That_Cannot_Verify()
    {
        var compileMock = CreateGate("compile", true, canVerify: false, passes: true);
        var testMock = CreateGate("test", true, canVerify: true, passes: true);

        var pipeline = new VerificationPipeline([compileMock.Object, testMock.Object]);
        var result = await pipeline.RunAsync(
            _containerMock.Object, "container-1", "/workspace",
            ["compile", "test"], null, CancellationToken.None);

        Assert.True(result.AllPassed);
        Assert.Single(result.Gates);
        Assert.Equal("test", result.Gates[0].Name);

        // Compile gate's VerifyAsync should never be called
        compileMock.Verify(g => g.VerifyAsync(
            It.IsAny<IContainerManager>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_Filters_By_GateFilter()
    {
        var compileMock = CreateGate("compile", true, true, true);
        var testMock = CreateGate("test", true, true, true);
        var securityMock = CreateGate("security", true, true, true);

        var pipeline = new VerificationPipeline([compileMock.Object, testMock.Object, securityMock.Object]);

        // Only run compile and test, not security
        var result = await pipeline.RunAsync(
            _containerMock.Object, "container-1", "/workspace",
            ["compile", "test"], null, CancellationToken.None);

        Assert.Equal(2, result.Gates.Count);
        Assert.DoesNotContain(result.Gates, g => g.Name == "security");
    }

    [Fact]
    public async Task RunAsync_Empty_GateFilter_Returns_No_Results()
    {
        var gates = new[] { CreateGate("compile", true, true, true).Object };
        var pipeline = new VerificationPipeline(gates);

        var result = await pipeline.RunAsync(
            _containerMock.Object, "container-1", "/workspace",
            [], null, CancellationToken.None);

        Assert.True(result.AllPassed);
        Assert.Empty(result.Gates);
        Assert.True(result.IsInconclusive);
    }

    [Fact]
    public async Task RunAsync_Gate_Issues_Are_Preserved()
    {
        var issues = new[] { "Missing semicolon", "Unused variable" };
        var compileMock = CreateGate("compile", true, true, false, "Build failed", issues);

        var pipeline = new VerificationPipeline([compileMock.Object]);
        var result = await pipeline.RunAsync(
            _containerMock.Object, "container-1", "/workspace",
            ["compile"], null, CancellationToken.None);

        Assert.False(result.AllPassed);
        Assert.Equal("Build failed", result.Gates[0].Output);
        Assert.Equal(2, result.Gates[0].Issues.Length);
        Assert.Contains("Missing semicolon", result.Gates[0].Issues);
    }
}
