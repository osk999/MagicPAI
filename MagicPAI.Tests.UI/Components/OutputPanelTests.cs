using Bunit;
using MagicPAI.Studio.Components;

namespace MagicPAI.Tests.UI.Components;

public class OutputPanelTests : TestContext
{
    [Fact]
    public void Renders_EmptyInitially()
    {
        var cut = RenderComponent<OutputPanel>();

        var content = cut.Find(".output-content");
        Assert.Equal("", content.TextContent.Trim());
    }

    [Fact]
    public void AppendText_DisplaysAppendedContent()
    {
        var cut = RenderComponent<OutputPanel>();

        cut.Instance.AppendText("Hello ");
        cut.Instance.AppendText("World");
        cut.Render();

        Assert.Contains("Hello World", cut.Find(".output-content").TextContent);
    }

    [Fact]
    public void SetText_ReplacesAllContent()
    {
        var cut = RenderComponent<OutputPanel>();

        cut.Instance.AppendText("old content");
        cut.Instance.SetText("new content");
        cut.Render();

        var text = cut.Find(".output-content").TextContent;
        Assert.Contains("new content", text);
        Assert.DoesNotContain("old content", text);
    }

    [Fact]
    public void Clear_RemovesAllContent()
    {
        var cut = RenderComponent<OutputPanel>();

        cut.Instance.AppendText("some content");
        cut.Render();

        // Click the Clear button (which calls Clear() on the render thread)
        cut.Find("button").Click();

        Assert.Equal("", cut.Find(".output-content").TextContent.Trim());
    }

    [Fact]
    public void HasClearButton()
    {
        var cut = RenderComponent<OutputPanel>();

        var buttons = cut.FindAll("button");
        Assert.Contains(buttons, b => b.TextContent.Contains("Clear"));
    }
}
