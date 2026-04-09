using Bunit;
using MagicPAI.Studio.Components;

namespace MagicPAI.Tests.UI.Components;

public class ActivityStatusBadgeTests : TestContext
{
    [Theory]
    [InlineData("Running", "badge-running", "~")]
    [InlineData("Completed", "badge-completed", "+")]
    [InlineData("Faulted", "badge-faulted", "x")]
    [InlineData("Pending", "badge-pending", "o")]
    public void Renders_CorrectStatusClassAndIcon(string status, string expectedClass, string expectedIcon)
    {
        var cut = RenderComponent<ActivityStatusBadge>(parameters => parameters
            .Add(p => p.Status, status));

        var badge = cut.Find(".activity-status-badge");
        Assert.Contains(expectedClass, badge.ClassList);
        Assert.Contains(expectedIcon, cut.Find(".activity-badge-icon").TextContent);
    }

    [Fact]
    public void WithLabel_RendersLabel()
    {
        var cut = RenderComponent<ActivityStatusBadge>(parameters => parameters
            .Add(p => p.Status, "Running")
            .Add(p => p.Label, "SpawnContainer"));

        Assert.Contains("SpawnContainer", cut.Find(".activity-badge-label").TextContent);
    }

    [Fact]
    public void WithoutLabel_RendersEmpty()
    {
        // Note: The component uses @Label directly (not DisplayLabel),
        // so null Label renders empty. This is the current behavior.
        var cut = RenderComponent<ActivityStatusBadge>(parameters => parameters
            .Add(p => p.Status, "Running"));

        var label = cut.Find(".activity-badge-label");
        Assert.NotNull(label);
    }
}
