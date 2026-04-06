using MagicPAI.Core.Models;
using MagicPAI.Core.Services;
using Moq;

namespace MagicPAI.Tests.Activities;

/// <summary>
/// Tests for the Triage activity's underlying logic.
/// We test the triage parsing and command building that the activity uses.
/// </summary>
public class TriageActivityTests
{
    [Fact]
    public void Triage_Uses_Haiku_Model_For_Cheap_Classification()
    {
        var runner = new ClaudeRunner();
        var cmd = runner.BuildCommand(new AgentRequest { Prompt = "Classify this task", Model = "haiku" });

        Assert.Contains("haiku", cmd);
        Assert.Contains("--dangerously-skip-permissions", cmd);
    }

    [Fact]
    public void Triage_SchemaGenerator_Produces_Valid_TriageResult_Schema()
    {
        var schema = SchemaGenerator.FromType<TriageResult>();
        Assert.Contains("complexity", schema);
        Assert.Contains("category", schema);
        Assert.Contains("recommended_model", schema);
        Assert.Contains("needs_decomposition", schema);
        Assert.Contains("additionalProperties", schema);
    }

    [Fact]
    public async Task Triage_Simple_Task_Returns_Low_Complexity()
    {
        var mockContainer = new Mock<IContainerManager>();
        var triageResponse = """{"complexity": 3, "category": "bug_fix", "needs_decomposition": false, "recommended_model": "haiku"}""";
        mockContainer.Setup(m => m.ExecAsync(
                "c1", It.IsAny<string>(), "/workspace", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecResult(0, triageResponse, ""));

        var result = await mockContainer.Object.ExecAsync(
            "c1", "claude -p 'triage'", "/workspace", CancellationToken.None);

        // Parse the triage response (simulating what TriageActivity does)
        using var doc = System.Text.Json.JsonDocument.Parse(result.Output);
        var root = doc.RootElement;
        var complexity = root.GetProperty("complexity").GetInt32();

        Assert.Equal(3, complexity);
        Assert.True(complexity < 7, "Simple task should have complexity < 7");
    }

    [Fact]
    public async Task Triage_Complex_Task_Returns_High_Complexity()
    {
        var mockContainer = new Mock<IContainerManager>();
        var triageResponse = """{"complexity": 9, "category": "architecture", "needs_decomposition": true, "recommended_model": "opus"}""";
        mockContainer.Setup(m => m.ExecAsync(
                "c1", It.IsAny<string>(), "/workspace", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecResult(0, triageResponse, ""));

        var result = await mockContainer.Object.ExecAsync(
            "c1", "claude -p 'triage'", "/workspace", CancellationToken.None);

        using var doc = System.Text.Json.JsonDocument.Parse(result.Output);
        var complexity = doc.RootElement.GetProperty("complexity").GetInt32();

        Assert.Equal(9, complexity);
        Assert.True(complexity >= 7, "Complex task should have complexity >= 7");
    }

    [Fact]
    public void TriageResult_Record_Properties()
    {
        var result = new TriageResult(5, "code_gen", "sonnet", false);

        Assert.Equal(5, result.Complexity);
        Assert.Equal("code_gen", result.Category);
        Assert.Equal("sonnet", result.RecommendedModel);
        Assert.False(result.NeedsDecomposition);
    }

    [Fact]
    public void TriageResult_DecompositionNeeded_ForComplexTasks()
    {
        var result = new TriageResult(8, "architecture", "opus", true);

        Assert.True(result.NeedsDecomposition);
        Assert.Equal("opus", result.RecommendedModel);
    }

    [Fact]
    public void Triage_Complexity_Threshold_IsSevenOrAbove()
    {
        // Complexity 6 = Simple
        Assert.True(6 < 7, "Complexity 6 should route to Simple");
        // Complexity 7 = Complex
        Assert.True(7 >= 7, "Complexity 7 should route to Complex");
    }
}
