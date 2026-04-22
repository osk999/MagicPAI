using MagicPAI.Core.Models;
using MagicPAI.Core.Services;

namespace MagicPAI.Tests.Services;

public class ClaudeRunnerTests
{
    private readonly ClaudeRunner _runner = new();

    [Fact]
    public void AgentName_Returns_Claude()
    {
        Assert.Equal("claude", _runner.AgentName);
    }

    [Fact]
    public void DefaultModel_Returns_Sonnet()
    {
        Assert.Equal("sonnet", _runner.DefaultModel);
    }

    [Fact]
    public void AvailableModels_Contains_Expected()
    {
        var models = _runner.AvailableModels;
        Assert.Contains("haiku", models);
        Assert.Contains("sonnet", models);
        Assert.Contains("opus", models);
    }

    [Fact]
    public void BuildCommand_Contains_Required_Flags()
    {
        var cmd = _runner.BuildCommand(new AgentRequest
        {
            Prompt = "Fix the bug", Model = "sonnet", WorkDir = "/workspace"
        });

        Assert.Contains("/workspace", cmd);
        Assert.Contains("claude", cmd);
        Assert.Contains("--dangerously-skip-permissions", cmd);
        Assert.Contains("Fix the bug", cmd);
        Assert.Contains("--output-format stream-json", cmd);
        Assert.Contains("--include-partial-messages", cmd);
        Assert.Contains("--append-system-prompt", cmd);
        Assert.Contains("--verbose", cmd);
    }

    [Fact]
    public void BuildCommand_Resolves_Model_Alias()
    {
        var cmd = _runner.BuildCommand(new AgentRequest { Prompt = "test", Model = "sonnet" });
        Assert.Contains("--model claude-sonnet-4-6", cmd);
    }

    [Fact]
    public void BuildCommand_Uses_Custom_Model_When_Not_Alias()
    {
        var cmd = _runner.BuildCommand(new AgentRequest { Prompt = "test", Model = "claude-custom-model" });
        Assert.Contains("--model claude-custom-model", cmd);
    }

    [Fact]
    public void BuildCommand_Escapes_Quotes_In_Prompt()
    {
        if (OperatingSystem.IsWindows())
        {
            // Windows uses double quotes — test that prompt is quoted
            var cmd = _runner.BuildCommand(new AgentRequest { Prompt = "Fix the user's bug", Model = "sonnet" });
            Assert.Contains("\"Fix the user's bug\"", cmd);
        }
        else
        {
            // Linux/Mac uses single quotes with escaping
            var cmd = _runner.BuildCommand(new AgentRequest { Prompt = "Fix the user's bug", Model = "sonnet" });
            Assert.Contains("'\\''", cmd);
            Assert.DoesNotContain("user's", cmd);
        }
    }

    [Fact]
    public void BuildCommand_Includes_JsonSchema_When_Provided()
    {
        var schema = """{"type":"object","properties":{"score":{"type":"number"}},"required":["score"],"additionalProperties":false}""";
        var cmd = _runner.BuildCommand(new AgentRequest
        {
            Prompt = "Rate this", OutputSchema = schema
        });

        Assert.Contains("--json-schema", cmd);
        Assert.Contains("score", cmd);
    }

    [Fact]
    public void BuildCommand_No_JsonSchema_When_Null()
    {
        var cmd = _runner.BuildCommand(new AgentRequest { Prompt = "test" });
        Assert.DoesNotContain("--json-schema", cmd);
    }

    [Fact]
    public void BuildCommand_Includes_Budget_When_Set()
    {
        var cmd = _runner.BuildCommand(new AgentRequest { Prompt = "test", MaxBudgetUsd = 2.50m });
        Assert.Contains("--max-budget-usd 2.50", cmd);
    }

    [Fact]
    public void BuildCommand_Uses_Default_Model_When_Null()
    {
        var cmd = _runner.BuildCommand(new AgentRequest { Prompt = "test" });
        Assert.Contains("--model claude-sonnet-4-6", cmd);
    }

    [Fact]
    public void BuildExecutionPlan_Resumes_Session_When_Provided()
    {
        var plan = _runner.BuildExecutionPlan(new AgentRequest
        {
            Prompt = "continue",
            SessionId = "sess-123"
        });

        Assert.Equal("claude", plan.MainRequest.FileName);
        Assert.Contains("--include-partial-messages", plan.MainRequest.Arguments);
        Assert.Contains("--append-system-prompt", plan.MainRequest.Arguments);
        Assert.Contains("--resume", plan.MainRequest.Arguments);
        Assert.Contains("sess-123", plan.MainRequest.Arguments);
    }

    [Fact]
    public void BuildCommand_Resumes_Session_When_Provided()
    {
        var cmd = _runner.BuildCommand(new AgentRequest
        {
            Prompt = "continue",
            SessionId = "sess-123"
        });

        Assert.Contains("--resume", cmd);
        Assert.Contains("sess-123", cmd);
        Assert.DoesNotContain("--session-id", cmd);
    }

    [Fact]
    public void ParseResponse_Returns_Failure_When_No_Result_Line()
    {
        var raw = "some random output\nnot json at all";
        var result = _runner.ParseResponse(raw);

        Assert.False(result.Success);
        Assert.Equal(raw, result.Output);
        Assert.Equal(0m, result.CostUsd);
        Assert.Empty(result.FilesModified);
    }

    [Fact]
    public void ParseResponse_Parses_Valid_Result_Json()
    {
        var json = """
            {"type": "assistant", "text": "working..."}
            {"type": "result", "result": "Task completed successfully", "is_error": false, "total_cost_usd": 0.0234, "usage": {"input_tokens": 1500, "output_tokens": 800}, "session_id": "sess-123", "files_modified": ["src/main.cs", "tests/test.cs"]}
            """;

        var result = _runner.ParseResponse(json);

        Assert.True(result.Success);
        Assert.Equal("Task completed successfully", result.Output);
        Assert.Equal(0.0234m, result.CostUsd);
        Assert.Equal(1500, result.InputTokens);
        Assert.Equal(800, result.OutputTokens);
        Assert.Equal("sess-123", result.SessionId);
        Assert.Equal(2, result.FilesModified.Length);
    }

    [Fact]
    public void ParseResponse_Handles_Error_Result()
    {
        var json = """
            {"type": "result", "result": "Something went wrong", "is_error": true, "total_cost_usd": 0.001}
            """;

        var result = _runner.ParseResponse(json);

        Assert.False(result.Success);
        Assert.Equal("Something went wrong", result.Output);
        Assert.Equal(0.001m, result.CostUsd);
    }

    [Fact]
    public void ParseResponse_Uses_Last_Result_When_Multiple_Exist()
    {
        var json = """
            {"type": "result", "result": "first", "is_error": false}
            {"type": "result", "result": "second", "is_error": false, "total_cost_usd": 0.05}
            """;

        var result = _runner.ParseResponse(json);

        Assert.True(result.Success);
        Assert.Equal("second", result.Output);
        Assert.Equal(0.05m, result.CostUsd);
    }

    [Fact]
    public void ParseResponse_Handles_Missing_Optional_Fields()
    {
        var json = """{"type": "result", "result": "done"}""";

        var result = _runner.ParseResponse(json);

        Assert.True(result.Success);
        Assert.Equal("done", result.Output);
        Assert.Equal(0m, result.CostUsd);
        Assert.Equal(0, result.InputTokens);
        Assert.Equal(0, result.OutputTokens);
        Assert.Null(result.SessionId);
        Assert.Empty(result.FilesModified);
    }

    [Fact]
    public void ParseResponse_Extracts_StructuredOutput_When_Present()
    {
        var json = """{"type": "result", "result": "done", "is_error": false, "structured_output": {"score": 8, "category": "code_gen"}}""";

        var result = _runner.ParseResponse(json);

        Assert.True(result.Success);
        Assert.Contains("score", result.Output);
        Assert.Contains("8", result.Output);
        Assert.Contains("score", result.StructuredOutputJson);
    }

    [Fact]
    public void BuildExecutionPlan_OversizePrompt_RoutesThroughStdin()
    {
        // When the prompt would overflow the Windows ~32 KB argv limit, the
        // runner must drop the prompt from argv, keep `-p` (which makes the
        // Claude CLI read the prompt from stdin), and stash the prompt on
        // ContainerExecRequest.StdinInput for the runner to pipe in.
        var oversize = new string('x', 40_000);
        var plan = _runner.BuildExecutionPlan(new AgentRequest
        {
            Prompt = oversize,
            Model = "claude-sonnet-4-6",
            WorkDir = "/workspace",
        });

        Assert.Contains("-p", plan.MainRequest.Arguments);
        Assert.DoesNotContain(oversize, plan.MainRequest.Arguments);
        Assert.Equal(oversize, plan.MainRequest.StdinInput);
    }

    [Fact]
    public void BuildExecutionPlan_NormalPrompt_StdinInputIsNull()
    {
        var plan = _runner.BuildExecutionPlan(new AgentRequest
        {
            Prompt = "short prompt",
            Model = "claude-sonnet-4-6",
            WorkDir = "/workspace",
        });

        Assert.Null(plan.MainRequest.StdinInput);
        Assert.Contains("-p", plan.MainRequest.Arguments);
        Assert.Contains("short prompt", plan.MainRequest.Arguments);
    }

    [Fact]
    public void BuildExecutionPlan_NormalPrompt_Succeeds()
    {
        var plan = _runner.BuildExecutionPlan(new AgentRequest
        {
            Prompt = "Create hello.txt",
            Model = "claude-sonnet-4-6",
            WorkDir = "/workspace",
        });

        Assert.NotNull(plan.MainRequest);
        Assert.Contains("Create hello.txt", plan.MainRequest.Arguments);
    }
}
