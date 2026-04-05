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
        var cmd = _runner.BuildCommand("Fix the bug", "sonnet", 10, "/workspace");

        Assert.Contains("cd /workspace", cmd);
        Assert.Contains("claude", cmd);
        Assert.Contains("--dangerously-skip-permissions", cmd);
        Assert.Contains("Fix the bug", cmd);
        Assert.Contains("--max-turns 10", cmd);
        Assert.Contains("--output-format stream-json", cmd);
    }

    [Fact]
    public void BuildCommand_Resolves_Model_Alias()
    {
        var cmd = _runner.BuildCommand("test", "sonnet", 1, "/workspace");
        Assert.Contains("--model claude-sonnet-4-6-20250627", cmd);
    }

    [Fact]
    public void BuildCommand_Uses_Custom_Model_When_Not_Alias()
    {
        var cmd = _runner.BuildCommand("test", "claude-custom-model", 1, "/workspace");
        Assert.Contains("--model claude-claude-custom-model", cmd);
    }

    [Fact]
    public void BuildCommand_Escapes_Single_Quotes_In_Prompt()
    {
        var cmd = _runner.BuildCommand("Fix the user's bug", "sonnet", 5, "/workspace");
        Assert.Contains("'\\''", cmd);
        Assert.DoesNotContain("user's", cmd);
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
            {"type": "result", "result": "Task completed successfully", "is_error": false, "cost_usd": 0.0234, "usage": {"input_tokens": 1500, "output_tokens": 800}, "session_id": "sess-123", "files_modified": ["src/main.cs", "tests/test.cs"]}
            """;

        var result = _runner.ParseResponse(json);

        Assert.True(result.Success);
        Assert.Equal("Task completed successfully", result.Output);
        Assert.Equal(0.0234m, result.CostUsd);
        Assert.Equal(1500, result.InputTokens);
        Assert.Equal(800, result.OutputTokens);
        Assert.Equal("sess-123", result.SessionId);
        Assert.Equal(2, result.FilesModified.Length);
        Assert.Contains("src/main.cs", result.FilesModified);
        Assert.Contains("tests/test.cs", result.FilesModified);
    }

    [Fact]
    public void ParseResponse_Handles_Error_Result()
    {
        var json = """
            {"type": "result", "result": "Something went wrong", "is_error": true, "cost_usd": 0.001}
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
            {"type": "result", "result": "second", "is_error": false, "cost_usd": 0.05}
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
}
