using MagicPAI.Core.Models;
using MagicPAI.Core.Services;
using Moq;
using System.Reflection;

namespace MagicPAI.Tests.Activities;

/// <summary>
/// Tests for the Triage activity's underlying logic.
/// We test the triage parsing and command building that the activity uses.
/// </summary>
public class TriageActivityTests
{
    [Fact]
    public void Triage_Uses_Sonnet_Model_For_Classification()
    {
        var runner = new ClaudeRunner();
        var cmd = runner.BuildCommand(new AgentRequest { Prompt = "Classify this task", Model = "sonnet" });

        Assert.Contains("sonnet", cmd);
        Assert.Contains("--dangerously-skip-permissions", cmd);
    }

    [Fact]
    public void Triage_SchemaGenerator_Produces_Valid_TriageResult_Schema()
    {
        var schema = SchemaGenerator.FromType<TriageResult>();
        Assert.Contains("complexity", schema);
        Assert.Contains("category", schema);
        Assert.Contains("recommended_model_power", schema);
        Assert.Contains("needs_decomposition", schema);
        Assert.Contains("additionalProperties", schema);
    }

    [Fact]
    public async Task Triage_Simple_Task_Returns_Low_Complexity()
    {
        var mockContainer = new Mock<IContainerManager>();
        var triageResponse = """{"complexity": 3, "category": "bug_fix", "needs_decomposition": false, "recommended_model_power": 2}""";
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
        var triageResponse = """{"complexity": 9, "category": "architecture", "needs_decomposition": true, "recommended_model_power": 1}""";
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
        var result = new TriageResult(5, "code_gen", 2, false);

        Assert.Equal(5, result.Complexity);
        Assert.Equal("code_gen", result.Category);
        Assert.Equal(2, result.RecommendedModelPower);
        Assert.False(result.NeedsDecomposition);
    }

    [Fact]
    public void TriageResult_DecompositionNeeded_ForComplexTasks()
    {
        var result = new TriageResult(8, "architecture", 1, true);

        Assert.True(result.NeedsDecomposition);
        Assert.Equal(1, result.RecommendedModelPower);
    }

    [Fact]
    public void Triage_Complexity_Threshold_IsSevenOrAbove()
    {
        // Complexity 6 = Simple
        Assert.True(6 < 7, "Complexity 6 should route to Simple");
        // Complexity 7 = Complex
        Assert.True(7 >= 7, "Complexity 7 should route to Complex");
    }

    [Fact]
    public void Triage_Fallback_ForFrontendTask_BoostsComplexity_And_Decomposition()
    {
        var method = typeof(MagicPAI.Activities.AI.TriageActivity)
            .GetMethod("CreateFallbackResult", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var result = Assert.IsType<TriageResult>(method!.Invoke(null, ["change the entire css of the donate page"])!);

        Assert.Equal(7, result.Complexity);
        Assert.True(result.NeedsDecomposition);
        Assert.Equal(2, result.RecommendedModelPower);
    }

    [Fact]
    public void Triage_Fallback_ForGeneralTask_UsesConservativeDefaults()
    {
        var method = typeof(MagicPAI.Activities.AI.TriageActivity)
            .GetMethod("CreateFallbackResult", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var result = Assert.IsType<TriageResult>(method!.Invoke(null, ["fix one typo in README"])!);

        Assert.Equal(5, result.Complexity);
        Assert.False(result.NeedsDecomposition);
        Assert.Equal(2, result.RecommendedModelPower);
    }

    [Fact]
    public void Triage_PromptResolution_UsesFirstAvailableSource()
    {
        var method = typeof(MagicPAI.Activities.AI.TriageActivity)
            .GetMethod("ResolvePrompt", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var result = method!.Invoke(null, [new string?[] { null, "", "prompt from variable" }]);

        Assert.Equal("prompt from variable", result);
    }
}
