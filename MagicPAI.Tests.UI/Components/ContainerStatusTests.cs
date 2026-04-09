using Bunit;
using MagicPAI.Studio.Components;

namespace MagicPAI.Tests.UI.Components;

public class ContainerStatusTests : TestContext
{
    [Fact]
    public void NoContainerId_ShowsNoContainer()
    {
        var cut = RenderComponent<ContainerStatus>(parameters => parameters
            .Add(p => p.ContainerId, ""));

        Assert.Contains("No Container", cut.Markup);
    }

    [Fact]
    public void WithContainerId_ShowsTruncatedId()
    {
        var longId = "abc123def456ghi789";
        var cut = RenderComponent<ContainerStatus>(parameters => parameters
            .Add(p => p.ContainerId, longId));

        // Should truncate to first 12 chars
        Assert.Contains("abc123def456", cut.Markup);
        Assert.DoesNotContain(longId, cut.Markup);
    }

    [Fact]
    public void ShortContainerId_ShowsFullId()
    {
        var cut = RenderComponent<ContainerStatus>(parameters => parameters
            .Add(p => p.ContainerId, "short"));

        Assert.Contains("short", cut.Markup);
    }

    [Fact]
    public void WithGuiUrl_ShowsVncLink()
    {
        var cut = RenderComponent<ContainerStatus>(parameters => parameters
            .Add(p => p.ContainerId, "ctr-123")
            .Add(p => p.GuiUrl, "http://localhost:6080"));

        var link = cut.Find(".vnc-link");
        Assert.Equal("http://localhost:6080", link.GetAttribute("href"));
        Assert.Contains("noVNC", link.TextContent);
    }

    [Fact]
    public void NoGuiUrl_HidesVncLink()
    {
        var cut = RenderComponent<ContainerStatus>(parameters => parameters
            .Add(p => p.ContainerId, "ctr-123")
            .Add(p => p.GuiUrl, null));

        Assert.Empty(cut.FindAll(".vnc-link"));
    }
}
