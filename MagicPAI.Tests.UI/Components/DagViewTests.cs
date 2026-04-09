using Bunit;
using MagicPAI.Studio.Components;

namespace MagicPAI.Tests.UI.Components;

public class DagViewTests : TestContext
{
    [Fact]
    public void NoActivities_ShowsEmptyState()
    {
        var cut = RenderComponent<DagView>(parameters => parameters
            .Add(p => p.Activities, new List<(string, string)>()));

        Assert.Contains("No activity data yet", cut.Markup);
        Assert.Empty(cut.FindAll(".dag-node"));
    }

    [Fact]
    public void RunningActivity_ShowsRunningIcon()
    {
        var cut = RenderComponent<DagView>(parameters => parameters
            .Add(p => p.Activities, new List<(string, string)>
            {
                ("SpawnContainer", "running")
            }));

        var node = cut.Find(".dag-node");
        Assert.Contains("running", node.ClassList);
        Assert.Contains("[*]", cut.Find(".dag-status-icon").TextContent);
        Assert.Contains("SpawnContainer", cut.Find(".dag-name").TextContent);
    }

    [Fact]
    public void CompletedActivity_ShowsCompletedIcon()
    {
        var cut = RenderComponent<DagView>(parameters => parameters
            .Add(p => p.Activities, new List<(string, string)>
            {
                ("SpawnContainer", "completed")
            }));

        Assert.Contains("[+]", cut.Find(".dag-status-icon").TextContent);
    }

    [Fact]
    public void FailedActivity_ShowsFailedIcon()
    {
        var cut = RenderComponent<DagView>(parameters => parameters
            .Add(p => p.Activities, new List<(string, string)>
            {
                ("RunCliAgent", "failed")
            }));

        Assert.Contains("[x]", cut.Find(".dag-status-icon").TextContent);
    }

    [Fact]
    public void MultipleActivities_RendersAll()
    {
        var cut = RenderComponent<DagView>(parameters => parameters
            .Add(p => p.Activities, new List<(string, string)>
            {
                ("SpawnContainer", "completed"),
                ("RunCliAgent", "running"),
                ("RunVerification", "pending")
            }));

        var nodes = cut.FindAll(".dag-node");
        Assert.Equal(3, nodes.Count);
    }
}
