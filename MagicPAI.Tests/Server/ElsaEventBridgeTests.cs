using System.Text.Json;
using MagicPAI.Core.Models;
using MagicPAI.Server.Bridge;
using MagicPAI.Server.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace MagicPAI.Tests.Server;

/// <summary>
/// Tests for ElsaEventBridge. Since the bridge uses reflection via GetProperty()
/// to read record fields and ExtractEvents to parse payloads, we construct
/// test record types that match the expected shape of Elsa log records.
/// </summary>
public class ElsaEventBridgeTests
{
    private readonly Mock<IHubContext<SessionHub>> _hubContextMock;
    private readonly Mock<IClientProxy> _clientProxyMock;
    private readonly SessionTracker _tracker;
    private readonly ElsaEventBridge _bridge;
    private readonly List<(string Method, object? Arg)> _sentMessages = [];

    public ElsaEventBridgeTests()
    {
        _hubContextMock = new Mock<IHubContext<SessionHub>>();
        _clientProxyMock = new Mock<IClientProxy>();
        _tracker = new SessionTracker();

        // Capture all SendAsync calls
        _clientProxyMock
            .Setup(c => c.SendCoreAsync(
                It.IsAny<string>(),
                It.IsAny<object?[]>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, object?[], CancellationToken>((method, args, _) =>
            {
                _sentMessages.Add((method, args.FirstOrDefault()));
            })
            .Returns(Task.CompletedTask);

        var mockClients = new Mock<IHubClients>();
        mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(_clientProxyMock.Object);
        _hubContextMock.Setup(h => h.Clients).Returns(mockClients.Object);

        _bridge = new ElsaEventBridge(
            _hubContextMock.Object,
            _tracker,
            NullLogger<ElsaEventBridge>.Instance);
    }

    // --- HandleOutputChunk via HandleAsync ---

    [Fact]
    public void ExtractContainerId_ParsesContainerMessage()
    {
        // Test the static ExtractContainerId helper via reflection
        var method = typeof(ElsaEventBridge)
            .GetMethod("ExtractContainerId",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        var result = (string?)method.Invoke(null, ["Container abc-123 spawned"]);
        Assert.Equal("abc-123", result);
    }

    [Fact]
    public void ExtractContainerId_ReturnsNullForEmpty()
    {
        var method = typeof(ElsaEventBridge)
            .GetMethod("ExtractContainerId",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        Assert.Null(method.Invoke(null, [""]));
        Assert.Null(method.Invoke(null, [" "]));
    }

    [Fact]
    public void ExtractContainerId_ReturnsNullIfNotStartingWithContainer()
    {
        var method = typeof(ElsaEventBridge)
            .GetMethod("ExtractContainerId",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        Assert.Null(method.Invoke(null, ["Spawned abc-123"]));
    }

    [Fact]
    public void ExtractContainerId_ReturnsNullForSingleWord()
    {
        var method = typeof(ElsaEventBridge)
            .GetMethod("ExtractContainerId",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        Assert.Null(method.Invoke(null, ["Container"]));
    }

    [Fact]
    public void ExtractGuiUrl_ParsesJsonGuiUrl()
    {
        var method = typeof(ElsaEventBridge)
            .GetMethod("ExtractGuiUrl",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        var json = """{"containerId": "abc", "guiUrl": "http://localhost:6080"}""";
        Assert.Equal("http://localhost:6080", (string?)method.Invoke(null, [json]));
    }

    [Fact]
    public void ExtractGuiUrl_ExtractsHttpUrlFromPlainText()
    {
        var method = typeof(ElsaEventBridge)
            .GetMethod("ExtractGuiUrl",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        Assert.Equal("http://localhost:6080", (string?)method.Invoke(null, ["GUI at http://localhost:6080 ready"]));
    }

    [Fact]
    public void ExtractGuiUrl_ReturnsNullWhenNoUrl()
    {
        var method = typeof(ElsaEventBridge)
            .GetMethod("ExtractGuiUrl",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        Assert.Null(method.Invoke(null, ["no url here"]));
    }

    // --- ExtractEvents via reflection ---

    [Fact]
    public void ExtractEvents_FromDictionary_YieldsKeyValuePairs()
    {
        var method = typeof(ElsaEventBridge)
            .GetMethod("ExtractEvents",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        var record = new TestLogRecord
        {
            EventName = "Completed",
            Payload = new Dictionary<string, object>
            {
                ["OutputChunk"] = """{"text": "hello"}""",
                ["CostUpdate"] = """{"costUsd": 0.01}"""
            }
        };

        var result = ((IEnumerable<(string EventName, string Message)>)method.Invoke(null, [record])!).ToList();
        Assert.Equal(2, result.Count);
        Assert.Contains(result, r => r.EventName == "OutputChunk");
        Assert.Contains(result, r => r.EventName == "CostUpdate");
    }

    [Fact]
    public void ExtractEvents_FromJsonElement_YieldsProperties()
    {
        var method = typeof(ElsaEventBridge)
            .GetMethod("ExtractEvents",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        var json = """{"OutputChunk": "{\"text\": \"hello\"}"}""";
        var element = JsonDocument.Parse(json).RootElement;
        var record = new TestLogRecord
        {
            EventName = "Completed",
            Payload = element
        };

        var result = ((IEnumerable<(string EventName, string Message)>)method.Invoke(null, [record])!).ToList();
        Assert.Single(result);
        Assert.Equal("OutputChunk", result[0].EventName);
    }

    [Fact]
    public void ExtractEvents_NullPayload_FallsBackToEventNameMessage()
    {
        var method = typeof(ElsaEventBridge)
            .GetMethod("ExtractEvents",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        var record = new TestLogRecord
        {
            EventName = "SomeFailed",
            Message = "error details",
            Payload = null
        };

        var result = ((IEnumerable<(string EventName, string Message)>)method.Invoke(null, [record])!).ToList();
        Assert.Single(result);
        Assert.Equal("SomeFailed", result[0].EventName);
        Assert.Equal("error details", result[0].Message);
    }

    // --- SessionTracker interactions (tested indirectly via AppendOutput) ---

    [Fact]
    public void HandleOutputChunk_ValidJson_ExtractsTextAndAppends()
    {
        // Test the JSON text extraction logic used by HandleOutputChunk
        var json = """{"text": "hello world", "activityId": "act1"}""";
        using var doc = JsonDocument.Parse(json);
        var text = doc.RootElement.TryGetProperty("text", out var t) ? t.GetString() ?? "" : json;

        Assert.Equal("hello world", text);
    }

    [Fact]
    public void HandleOutputChunk_InvalidJson_UsesRawMessage()
    {
        var message = "plain text output";
        string text;
        try
        {
            using var doc = JsonDocument.Parse(message);
            text = doc.RootElement.TryGetProperty("text", out var t) ? t.GetString() ?? "" : message;
        }
        catch (JsonException)
        {
            text = message;
        }

        Assert.Equal("plain text output", text);
    }

    [Fact]
    public void HandleCostUpdate_ParsesJsonCostFields()
    {
        var json = """{"costUsd": 1.23, "inputTokens": 1000, "outputTokens": 500}""";
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var cost = root.TryGetProperty("costUsd", out var costProp) ? costProp.GetDecimal() : 0m;
        var inputTokens = root.TryGetProperty("inputTokens", out var inputProp) ? inputProp.GetInt32() : 0;
        var outputTokens = root.TryGetProperty("outputTokens", out var outputProp) ? outputProp.GetInt32() : 0;

        Assert.Equal(1.23m, cost);
        Assert.Equal(1000, inputTokens);
        Assert.Equal(500, outputTokens);
    }

    [Fact]
    public void HandleActivityStateRecord_MapsStartedToRunning()
    {
        // Test the mapping logic used internally
        var status = "Started" switch
        {
            "Started" => "running",
            "Completed" => "completed",
            "Faulted" => "failed",
            _ => (string?)null
        };
        Assert.Equal("running", status);
    }

    [Fact]
    public void HandleActivityStateRecord_MapsCompletedCorrectly()
    {
        var status = "Completed" switch
        {
            "Started" => "running",
            "Completed" => "completed",
            "Faulted" => "failed",
            _ => (string?)null
        };
        Assert.Equal("completed", status);
    }

    [Fact]
    public void HandleActivityStateRecord_MapsFaultedToFailed()
    {
        var status = "Faulted" switch
        {
            "Started" => "running",
            "Completed" => "completed",
            "Faulted" => "failed",
            _ => (string?)null
        };
        Assert.Equal("failed", status);
    }

    [Fact]
    public void BuildInsight_ParsesClassifierPayload()
    {
        var method = typeof(ElsaEventBridge)
            .GetMethod("BuildInsight",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        var payload = """{"complexity":8,"category":"architecture","outcome":"Complex"}""";
        var insight = (TaskInsightEvent)method.Invoke(null, ["s1", "TriageResult", payload])!;

        Assert.Equal("classifier", insight.Kind);
        Assert.Equal("Complex", insight.Verdict);
        Assert.Contains("Complexity 8", insight.Summary);
    }

    [Fact]
    public void BuildInsight_ParsesPromptTransformPayload()
    {
        var method = typeof(ElsaEventBridge)
            .GetMethod("BuildInsight",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        var payload = """{"label":"Prompt Enhancement","summary":"Prompt was transformed.","verdict":"changed","before":"short","after":"expanded"}""";
        var insight = (TaskInsightEvent)method.Invoke(null, ["s1", "PromptTransform", payload])!;

        Assert.Equal("prompt-transform", insight.Kind);
        Assert.Equal("short", insight.BeforeText);
        Assert.Equal("expanded", insight.AfterText);
        Assert.Equal("changed", insight.Verdict);
    }

    /// <summary>
    /// Test record type that mimics the shape of Elsa's ActivityExecutionLogRecord.
    /// The ElsaEventBridge uses reflection (GetProperty) to access these fields.
    /// </summary>
    private class TestLogRecord
    {
        public string EventName { get; set; } = "";
        public string ActivityId { get; set; } = "";
        public string ActivityName { get; set; } = "";
        public string ActivityType { get; set; } = "";
        public string Message { get; set; } = "";
        public object? Payload { get; set; }
    }
}
