using MagicPAI.Activities.AI;

namespace MagicPAI.Tests.Activities;

public class ResearchPromptActivityTests
{
    [Fact]
    public void BuildCodebaseAnalysisPrompt_Includes_Original_And_Enhanced_Prompt()
    {
        var result = ResearchPromptActivity.BuildCodebaseAnalysisPrompt("fix login", "fix login in auth flow");

        Assert.Contains("## Original Prompt", result);
        Assert.Contains("fix login", result);
        Assert.Contains("## Enhanced Prompt", result);
        Assert.Contains("fix login in auth flow", result);
    }

    [Fact]
    public void BuildResearchContextPrompt_Includes_Codebase_Analysis()
    {
        var result = ResearchPromptActivity.BuildResearchContextPrompt(
            "fix login",
            "fix login in auth flow",
            "AuthController and SessionService look relevant.");

        Assert.Contains("repo-map style execution brief", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AuthController and SessionService look relevant.", result);
        Assert.Contains("verification steps", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildFinalPrompt_Requires_Structured_Json_Output()
    {
        var result = ResearchPromptActivity.BuildFinalPrompt(
            "fix login",
            "fix login in auth flow",
            "AuthController and SessionService look relevant.",
            "Inspect API auth flow and add regression coverage.");

        Assert.Contains("Return JSON only", result);
        Assert.Contains("\"enhanced_prompt\"", result);
        Assert.Contains("acceptance criteria", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("verification steps", result, StringComparison.OrdinalIgnoreCase);
    }
}
