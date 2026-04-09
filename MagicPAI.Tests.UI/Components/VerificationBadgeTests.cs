using Bunit;
using MagicPAI.Studio.Components;

namespace MagicPAI.Tests.UI.Components;

public class VerificationBadgeTests : TestContext
{
    [Fact]
    public void PassedGate_ShowsPassBadge()
    {
        var cut = RenderComponent<VerificationBadge>(parameters => parameters
            .Add(p => p.GateName, "compile")
            .Add(p => p.Passed, true)
            .Add(p => p.Output, "Build succeeded")
            .Add(p => p.Issues, Array.Empty<string>()));

        var badge = cut.Find(".verification-badge");
        Assert.Contains("passed", badge.ClassList);
        Assert.Contains("PASS", cut.Find(".badge-icon").TextContent);
        Assert.Contains("compile", cut.Find(".badge-name").TextContent);
    }

    [Fact]
    public void FailedGate_ShowsFailBadge()
    {
        var cut = RenderComponent<VerificationBadge>(parameters => parameters
            .Add(p => p.GateName, "test")
            .Add(p => p.Passed, false)
            .Add(p => p.Output, "3 tests failed")
            .Add(p => p.Issues, ["Test1 failed", "Test2 failed"]));

        var badge = cut.Find(".verification-badge");
        Assert.Contains("failed", badge.ClassList);
        Assert.Contains("FAIL", cut.Find(".badge-icon").TextContent);
    }

    [Fact]
    public void WithIssues_RendersIssueList()
    {
        var cut = RenderComponent<VerificationBadge>(parameters => parameters
            .Add(p => p.GateName, "test")
            .Add(p => p.Passed, false)
            .Add(p => p.Output, "")
            .Add(p => p.Issues, ["Issue1", "Issue2", "Issue3"]));

        var issues = cut.FindAll(".badge-issues li");
        Assert.Equal(3, issues.Count);
    }

    [Fact]
    public void NoIssues_HidesIssueList()
    {
        var cut = RenderComponent<VerificationBadge>(parameters => parameters
            .Add(p => p.GateName, "compile")
            .Add(p => p.Passed, true)
            .Add(p => p.Output, "ok")
            .Add(p => p.Issues, Array.Empty<string>()));

        Assert.Empty(cut.FindAll(".badge-issues"));
    }
}
