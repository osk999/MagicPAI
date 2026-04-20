using System.Text;
using MagicPAI.Core.Services;

namespace MagicPAI.Tests.Services;

/// <summary>
/// Regression tests for <see cref="ClaudeRunner.ParseResponse"/> against
/// Windows-PTY-wrapped stream-json output. On Windows, `docker exec` pipes
/// stdout through a PTY that injects \r\n (or bare \r) inside JSON string
/// literals at ~256-char boundaries, which used to yield a 196KB raw blob as
/// <c>EnhancedPrompt</c> because Split('\n') produced unparseable lines.
/// </summary>
public class ClaudeRunnerParseResponseTests
{
    private readonly ClaudeRunner _runner = new();

    [Fact]
    public void ParseResponse_WellFormedStreamJson_ParsesResult()
    {
        var raw = string.Join("\n",
            """{"type":"system","subtype":"init","session_id":"s1"}""",
            """{"type":"assistant","message":{"content":[{"type":"text","text":"Working..."}]}}""",
            """{"type":"result","result":"All done.","is_error":false,"total_cost_usd":0.0123,"usage":{"input_tokens":100,"output_tokens":200},"session_id":"s1"}""");

        var parsed = _runner.ParseResponse(raw);

        Assert.True(parsed.Success);
        Assert.Equal("All done.", parsed.Output);
        Assert.Equal(0.0123m, parsed.CostUsd);
        Assert.Equal(100, parsed.InputTokens);
        Assert.Equal(200, parsed.OutputTokens);
        Assert.Equal("s1", parsed.SessionId);
    }

    [Fact]
    public void ParseResponse_PtyWrapAt256CharBoundaries_StillParsesResult()
    {
        // Build a stream-json line with a long result string, then inject \r\n
        // every ~256 chars to simulate a Windows PTY wrap. The wrap artifacts
        // should not prevent the balanced-brace scanner from reconstituting
        // the object and extracting `result`.
        var resultText = new string('a', 2000);  // 2000 chars — will be wrapped many times
        var obj =
            $$"""{"type":"result","result":"{{resultText}}","is_error":false,"total_cost_usd":0.5,"usage":{"input_tokens":1,"output_tokens":2},"session_id":"wrapped-session"}""";

        var wrapped = InjectPtyWrap(obj, every: 256);
        // Sanity: the wrapped payload must really contain the artifacts we're testing.
        Assert.Contains("\r\n", wrapped);

        var parsed = _runner.ParseResponse(wrapped);

        Assert.True(parsed.Success);
        Assert.Equal(resultText, parsed.Output);
        Assert.Equal(0.5m, parsed.CostUsd);
        Assert.Equal("wrapped-session", parsed.SessionId);
    }

    [Fact]
    public void ParseResponse_PtyWrapAcrossMultipleObjects_ParsesLastResult()
    {
        // Three objects separated by \n, each padded so the last object crosses
        // multiple 256-char boundaries. The scanner has to treat \r\n between
        // objects as a boundary but \r\n inside strings as artifacts.
        var padding = new string('x', 400);
        var obj1 = $$"""{"type":"system","subtype":"init","session_id":"s2","note":"{{padding}}"}""";
        var obj2 = $$"""{"type":"assistant","note":"{{padding}}"}""";
        var obj3 = $$"""{"type":"result","result":"{{padding}}","is_error":false,"total_cost_usd":1.23,"session_id":"s2"}""";

        var joined = string.Join("\n", obj1, obj2, obj3);
        var wrapped = InjectPtyWrap(joined, every: 256);

        var parsed = _runner.ParseResponse(wrapped);

        Assert.True(parsed.Success);
        Assert.Equal(padding, parsed.Output);
        Assert.Equal(1.23m, parsed.CostUsd);
        Assert.Equal("s2", parsed.SessionId);
    }

    [Fact]
    public void ParseResponse_NoResultEnvelope_ReturnsFailureWithCleanedOutput()
    {
        // Partial stream with no `type:result` — e.g. if the CLI was killed mid-response.
        var raw = string.Join("\n",
            """{"type":"system","subtype":"init","session_id":"s3"}""",
            """{"type":"assistant","message":{"content":[{"type":"text","text":"still working"}]}}""");

        var parsed = _runner.ParseResponse(raw);

        Assert.False(parsed.Success);
        // Cleaned output is returned so the caller can inspect/log it.
        Assert.NotNull(parsed.Output);
        Assert.Contains("type", parsed.Output);
        Assert.Equal(0m, parsed.CostUsd);
        Assert.Empty(parsed.FilesModified);
    }

    [Fact]
    public void ParseResponse_StructuredOutputInsideWrappedStream_Preserved()
    {
        // When --json-schema is used, the structured_output field may contain
        // a nested JSON object. Make sure PTY wraps inside that nested structure
        // don't break the outer parse.
        var innerJson = """{"complexity":7,"category":"refactor","rationale":"because of the bug in the PTY layer that kept getting injected at 256 byte boundaries even inside strings which is quite annoying"}""";
        var obj =
            $$"""{"type":"result","result":"done","is_error":false,"structured_output":{{innerJson}},"session_id":"s4"}""";
        var wrapped = InjectPtyWrap(obj, every: 200);

        var parsed = _runner.ParseResponse(wrapped);

        Assert.True(parsed.Success);
        Assert.NotNull(parsed.StructuredOutputJson);
        Assert.Contains("complexity", parsed.StructuredOutputJson!);
        Assert.Contains("refactor", parsed.StructuredOutputJson!);
    }

    [Fact]
    public void SplitBalancedJsonObjects_YieldsEachObject()
    {
        var raw = """{"a":1}{"b":2}{"c":3}""";
        var objs = ClaudeRunner.SplitBalancedJsonObjects(raw).ToList();

        Assert.Equal(3, objs.Count);
        Assert.Equal("""{"a":1}""", objs[0]);
        Assert.Equal("""{"b":2}""", objs[1]);
        Assert.Equal("""{"c":3}""", objs[2]);
    }

    [Fact]
    public void SplitBalancedJsonObjects_HandlesNestedBraces()
    {
        var raw = """{"outer":{"inner":{"deep":"value"}}}""";
        var objs = ClaudeRunner.SplitBalancedJsonObjects(raw).ToList();

        Assert.Single(objs);
        Assert.Equal(raw, objs[0]);
    }

    [Fact]
    public void SplitBalancedJsonObjects_IgnoresBracesInStrings()
    {
        // `{` and `}` inside the "note" string value must not change depth.
        var raw = """{"note":"this { is } not code","ok":true}""";
        var objs = ClaudeRunner.SplitBalancedJsonObjects(raw).ToList();

        Assert.Single(objs);
        Assert.Equal(raw, objs[0]);
    }

    [Fact]
    public void SplitBalancedJsonObjects_HandlesEscapedQuotesInStrings()
    {
        // Escaped quotes must not toggle inString state.
        var raw = """{"note":"she said \"hi\" and left","x":1}""";
        var objs = ClaudeRunner.SplitBalancedJsonObjects(raw).ToList();

        Assert.Single(objs);
        Assert.Equal(raw, objs[0]);
    }

    [Fact]
    public void SplitBalancedJsonObjects_StripsControlCharsInsideStrings()
    {
        // Simulate PTY wrap: \r\n inside a string literal. The scanner
        // should produce a clean, parse-able object with the wrap removed.
        var raw = "{\"note\":\"hello\r\nworld\"}";
        var objs = ClaudeRunner.SplitBalancedJsonObjects(raw).ToList();

        Assert.Single(objs);
        Assert.DoesNotContain("\r", objs[0]);
        Assert.DoesNotContain("\n", objs[0]);
        // The string must now be parseable.
        var parsed = System.Text.Json.JsonDocument.Parse(objs[0]);
        Assert.Equal("helloworld", parsed.RootElement.GetProperty("note").GetString());
    }

    /// <summary>
    /// Simulate a Windows PTY that injects \r\n at every Nth byte, even in
    /// the middle of a JSON string literal.
    /// </summary>
    private static string InjectPtyWrap(string raw, int every)
    {
        var sb = new StringBuilder(raw.Length + raw.Length / every * 2);
        for (int i = 0; i < raw.Length; i++)
        {
            sb.Append(raw[i]);
            if ((i + 1) % every == 0 && i + 1 < raw.Length)
                sb.Append("\r\n");
        }
        return sb.ToString();
    }
}
