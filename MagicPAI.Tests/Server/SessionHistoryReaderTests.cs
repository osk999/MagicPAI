using System.Reflection;
using MagicPAI.Server.Bridge;

namespace MagicPAI.Tests.Server;

/// <summary>
/// Tests the static/private helper methods in SessionHistoryReader via reflection.
/// Database-dependent methods are tested at the integration level.
/// </summary>
public class SessionHistoryReaderTests
{
    private static string InvokeMapState(string status, string? subStatus)
    {
        var method = typeof(SessionHistoryReader)
            .GetMethod("MapState", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (string)method.Invoke(null, [status, subStatus])!;
    }

    private static string InvokeFriendlyWorkflowName(string definitionId)
    {
        var method = typeof(SessionHistoryReader)
            .GetMethod("FriendlyWorkflowName", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (string)method.Invoke(null, [definitionId])!;
    }

    private static string InvokeExtractOutputText(string message)
    {
        var method = typeof(SessionHistoryReader)
            .GetMethod("ExtractOutputText", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (string)method.Invoke(null, [message])!;
    }

    // --- MapState ---

    [Fact]
    public void MapState_Running_ReturnsRunning()
    {
        Assert.Equal("running", InvokeMapState("Running", null));
    }

    [Fact]
    public void MapState_FinishedFinished_ReturnsCompleted()
    {
        Assert.Equal("completed", InvokeMapState("Finished", "Finished"));
    }

    [Fact]
    public void MapState_FinishedFaulted_ReturnsFailed()
    {
        Assert.Equal("failed", InvokeMapState("Finished", "Faulted"));
    }

    [Fact]
    public void MapState_FinishedCancelled_ReturnsCancelled()
    {
        Assert.Equal("cancelled", InvokeMapState("Finished", "Cancelled"));
    }

    [Fact]
    public void MapState_FinishedNoSubStatus_ReturnsCompleted()
    {
        Assert.Equal("completed", InvokeMapState("Finished", null));
    }

    [Fact]
    public void MapState_UnknownStatus_ReturnsUnknown()
    {
        Assert.Equal("unknown", InvokeMapState("Suspended", null));
    }

    // --- FriendlyWorkflowName ---

    [Fact]
    public void FriendlyWorkflowName_RemovesWorkflowSuffix()
    {
        Assert.Equal("full-orchestrate", InvokeFriendlyWorkflowName("FullOrchestrateWorkflow"));
    }

    [Fact]
    public void FriendlyWorkflowName_InsertsDashesAtUppercase()
    {
        Assert.Equal("simple-agent", InvokeFriendlyWorkflowName("SimpleAgentWorkflow"));
    }

    [Fact]
    public void FriendlyWorkflowName_NoWorkflowSuffix_StillConverts()
    {
        Assert.Equal("my-custom-thing", InvokeFriendlyWorkflowName("MyCustomThing"));
    }

    [Fact]
    public void FriendlyWorkflowName_SingleWord_LowersCase()
    {
        Assert.Equal("simple", InvokeFriendlyWorkflowName("SimpleWorkflow"));
    }

    // --- ExtractOutputText ---

    [Fact]
    public void ExtractOutputText_ParsesJsonText()
    {
        var json = """{"text": "hello world", "other": 42}""";
        Assert.Equal("hello world", InvokeExtractOutputText(json));
    }

    [Fact]
    public void ExtractOutputText_FallsBackToRawMessage()
    {
        Assert.Equal("plain text", InvokeExtractOutputText("plain text"));
    }

    [Fact]
    public void ExtractOutputText_JsonWithoutTextProperty_ReturnsRaw()
    {
        var json = """{"other": "value"}""";
        Assert.Equal(json, InvokeExtractOutputText(json));
    }

    [Fact]
    public void ExtractOutputText_NullTextProperty_ReturnsEmpty()
    {
        var json = """{"text": null}""";
        Assert.Equal("", InvokeExtractOutputText(json));
    }
}
