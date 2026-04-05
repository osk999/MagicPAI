using MagicPAI.Core.Models;
using MagicPAI.Core.Services;
using Moq;

namespace MagicPAI.Tests.Activities;

/// <summary>
/// Tests for the RunCliAgent activity's underlying logic.
/// Since Elsa ActivityExecutionContext is complex to mock,
/// we test the composed services (ICliAgentFactory + IContainerManager)
/// that the activity uses internally.
/// </summary>
public class RunCliAgentActivityTests
{
    [Fact]
    public void Factory_Creates_ClaudeRunner()
    {
        var factory = new CliAgentFactory();
        var runner = factory.Create("claude");

        Assert.Equal("claude", runner.AgentName);
        Assert.IsType<ClaudeRunner>(runner);
    }

    [Fact]
    public void Factory_Creates_CodexRunner()
    {
        var factory = new CliAgentFactory();
        var runner = factory.Create("codex");

        Assert.Equal("codex", runner.AgentName);
        Assert.IsType<CodexRunner>(runner);
    }

    [Fact]
    public void Factory_Creates_GeminiRunner()
    {
        var factory = new CliAgentFactory();
        var runner = factory.Create("gemini");

        Assert.Equal("gemini", runner.AgentName);
        Assert.IsType<GeminiRunner>(runner);
    }

    [Fact]
    public void Factory_Throws_For_Unknown_Agent()
    {
        var factory = new CliAgentFactory();
        Assert.Throws<ArgumentException>(() => factory.Create("unknown-agent"));
    }

    [Fact]
    public void Factory_AvailableAgents_Contains_All()
    {
        var factory = new CliAgentFactory();
        Assert.Contains("claude", factory.AvailableAgents);
        Assert.Contains("codex", factory.AvailableAgents);
        Assert.Contains("gemini", factory.AvailableAgents);
    }

    [Fact]
    public async Task RunCliAgent_Flow_BuildCommand_Then_ExecStreaming_Then_Parse()
    {
        // Simulate the full RunCliAgent activity flow using mocks
        var factory = new CliAgentFactory();
        var runner = factory.Create("claude");

        var command = runner.BuildCommand("Fix bug", "sonnet", 10, "/workspace");
        Assert.Contains("--dangerously-skip-permissions", command);

        // Mock container manager
        var mockContainer = new Mock<IContainerManager>();
        var streamJson = """{"type": "result", "result": "Bug fixed", "is_error": false, "cost_usd": 0.05}""";
        mockContainer.Setup(m => m.ExecStreamingAsync(
                "c1", command, It.IsAny<Action<string>>(),
                It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecResult(0, streamJson, ""));

        var execResult = await mockContainer.Object.ExecStreamingAsync(
            "c1", command, _ => { }, TimeSpan.FromMinutes(30), CancellationToken.None);

        var parsed = runner.ParseResponse(execResult.Output);
        Assert.True(parsed.Success);
        Assert.Equal("Bug fixed", parsed.Output);
        Assert.Equal(0.05m, parsed.CostUsd);
    }

    [Fact]
    public async Task RunCliAgent_Flow_FailedExecution_Returns_Failed()
    {
        var factory = new CliAgentFactory();
        var runner = factory.Create("claude");
        var command = runner.BuildCommand("Bad prompt", "haiku", 1, "/workspace");

        var mockContainer = new Mock<IContainerManager>();
        var errorJson = """{"type": "result", "result": "Error occurred", "is_error": true}""";
        mockContainer.Setup(m => m.ExecStreamingAsync(
                "c1", command, It.IsAny<Action<string>>(),
                It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecResult(1, errorJson, ""));

        var execResult = await mockContainer.Object.ExecStreamingAsync(
            "c1", command, _ => { }, TimeSpan.FromMinutes(30), CancellationToken.None);

        var parsed = runner.ParseResponse(execResult.Output);
        Assert.False(parsed.Success);
    }

    [Fact]
    public void Codex_BuildCommand_Contains_ApprovalMode()
    {
        var runner = new CodexRunner();
        var cmd = runner.BuildCommand("Build feature", "gpt-5.4", 5, "/workspace");

        Assert.Contains("--approval-mode full-auto", cmd);
        Assert.Contains("-m gpt-5.4", cmd);
    }

    [Fact]
    public void Gemini_BuildCommand_Contains_Sandbox_Flag()
    {
        var runner = new GeminiRunner();
        var cmd = runner.BuildCommand("Analyze code", "gemini-3.1-pro-preview", 5, "/workspace");

        Assert.Contains("--sandbox=false", cmd);
        Assert.Contains("--model gemini-3.1-pro-preview", cmd);
    }
}
