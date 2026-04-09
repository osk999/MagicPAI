using Bunit;
using MagicPAI.Studio.Components;

namespace MagicPAI.Tests.UI.Components;

public class AgentSelectorTests : TestContext
{
    [Fact]
    public void Renders_ThreeOptions()
    {
        var cut = RenderComponent<AgentSelector>(parameters => parameters
            .Add(p => p.Value, "claude"));

        var options = cut.FindAll("option");
        Assert.Equal(3, options.Count);
        Assert.Contains(options, o => o.GetAttribute("value") == "claude");
        Assert.Contains(options, o => o.GetAttribute("value") == "codex");
        Assert.Contains(options, o => o.GetAttribute("value") == "gemini");
    }

    [Fact]
    public void Selection_TriggersValueChanged()
    {
        string? changedValue = null;
        var cut = RenderComponent<AgentSelector>(parameters => parameters
            .Add(p => p.Value, "claude")
            .Add(p => p.ValueChanged, value => changedValue = value));

        cut.Find("select").Change("codex");

        Assert.Equal("codex", changedValue);
    }
}
