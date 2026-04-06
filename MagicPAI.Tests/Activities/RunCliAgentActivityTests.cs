using MagicPAI.Core.Models;
using MagicPAI.Core.Services;
using Moq;

namespace MagicPAI.Tests.Activities;

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
        var runner = new ClaudeRunner();
        var command = runner.BuildCommand(new AgentRequest { Prompt = "Fix bug", Model = "sonnet" });
        Assert.Contains("--dangerously-skip-permissions", command);

        var mockContainer = new Mock<IContainerManager>();
        var streamJson = """{"type": "result", "result": "Bug fixed", "is_error": false, "total_cost_usd": 0.05}""";
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
        var runner = new ClaudeRunner();
        var command = runner.BuildCommand(new AgentRequest { Prompt = "Bad prompt", Model = "haiku" });

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

    // --- Codex ---

    [Fact]
    public void Codex_BuildCommand_Contains_Exec_And_FullAccess()
    {
        var runner = new CodexRunner();
        var cmd = runner.BuildCommand(new AgentRequest { Prompt = "Build feature", Model = "gpt-5.4" });
        Assert.Contains("codex exec", cmd);
        Assert.Contains("--sandbox danger-full-access", cmd);
        Assert.Contains("ask_for_approval", cmd);
        Assert.Contains("never", cmd);
    }

    [Fact]
    public void Codex_BuildCommand_With_Schema_Writes_File()
    {
        var runner = new CodexRunner();
        var schema = """{"type":"object","properties":{"x":{"type":"number"}},"required":["x"],"additionalProperties":false}""";
        var cmd = runner.BuildCommand(new AgentRequest { Prompt = "classify", OutputSchema = schema });
        Assert.Contains("--output-schema", cmd);
        Assert.Contains("codex-schema", cmd);
    }

    [Fact]
    public void Codex_DefaultModel_Is_Gpt54()
    {
        var runner = new CodexRunner();
        Assert.Equal("gpt-5.4", runner.DefaultModel);
        Assert.Contains("gpt-5.4", runner.AvailableModels);
    }

    [Fact]
    public void Codex_ResolveModel_Aliases()
    {
        var runner = new CodexRunner();
        var cmd = runner.BuildCommand(new AgentRequest { Prompt = "test", Model = "gpt5" });
        Assert.Contains("-m gpt-5.4", cmd);
    }

    [Fact]
    public void Codex_ParseResponse_Detects_Error()
    {
        var runner = new CodexRunner();
        Assert.False(runner.ParseResponse("Something went wrong with error").Success);
    }

    [Fact]
    public void Codex_ParseResponse_Success_When_No_Error()
    {
        var runner = new CodexRunner();
        Assert.True(runner.ParseResponse("Task completed successfully").Success);
    }

    // --- Gemini ---

    [Fact]
    public void Gemini_BuildCommand_Contains_Yolo_Flag()
    {
        var runner = new GeminiRunner();
        var cmd = runner.BuildCommand(new AgentRequest { Prompt = "Analyze code", Model = "gemini-3.1-pro-preview" });
        Assert.Contains("--yolo", cmd);
        Assert.Contains("--model gemini-3.1-pro-preview", cmd);
        Assert.Contains("--output-format json", cmd);
    }

    [Fact]
    public void Gemini_BuildCommand_With_Schema_Embeds_Schema_In_Prompt()
    {
        var runner = new GeminiRunner();
        var schema = """{"type":"object","properties":{"x":{"type":"number"}}}""";
        var cmd = runner.BuildCommand(new AgentRequest { Prompt = "classify", OutputSchema = schema });
        // Gemini has no native --json-schema flag, so schema goes into the prompt
        Assert.False(runner.SupportsNativeSchema);
        Assert.Contains(schema, cmd); // schema is in the command (embedded in prompt)
    }

    [Fact]
    public void Claude_SupportsNativeSchema()
    {
        Assert.True(new ClaudeRunner().SupportsNativeSchema);
    }

    [Fact]
    public void Codex_SupportsNativeSchema()
    {
        Assert.True(new CodexRunner().SupportsNativeSchema);
    }

    [Fact]
    public void Gemini_DoesNotSupportNativeSchema()
    {
        Assert.False(new GeminiRunner().SupportsNativeSchema);
    }

    [Fact]
    public void Gemini_DefaultModel()
    {
        var runner = new GeminiRunner();
        Assert.Equal("gemini-3.1-pro-preview", runner.DefaultModel);
        Assert.Contains("gemini-3-flash", runner.AvailableModels);
    }

    [Fact]
    public void Gemini_ResolveModel_Aliases()
    {
        var runner = new GeminiRunner();
        var cmd = runner.BuildCommand(new AgentRequest { Prompt = "test", Model = "flash" });
        Assert.Contains("--model gemini-3-flash", cmd);
    }

    [Fact]
    public void Gemini_ParseResponse_Always_Succeeds()
    {
        var runner = new GeminiRunner();
        var result = runner.ParseResponse("any output");
        Assert.True(result.Success);
        Assert.Equal("any output", result.Output);
    }

    // --- SchemaGenerator ---

    [Fact]
    public void SchemaGenerator_FromType_Generates_Valid_Schema()
    {
        var schema = SchemaGenerator.FromType<TestDto>();
        Assert.Contains("\"type\":\"object\"", schema);
        Assert.Contains("\"name\"", schema);
        Assert.Contains("\"score\"", schema);
        Assert.Contains("\"is_active\"", schema);
        Assert.Contains("\"additionalProperties\":false", schema);
    }

    [Fact]
    public void SchemaGenerator_Converts_PropertyNames_To_SnakeCase()
    {
        var schema = SchemaGenerator.FromType<TestDto>();
        Assert.Contains("is_active", schema);
        Assert.DoesNotContain("IsActive", schema);
    }

    private class TestDto
    {
        public string Name { get; set; } = "";
        public int Score { get; set; }
        public bool IsActive { get; set; }
    }
}
