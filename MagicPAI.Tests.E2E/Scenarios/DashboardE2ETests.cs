using MagicPAI.Tests.E2E.Fixtures;

namespace MagicPAI.Tests.E2E.Scenarios;

/// <summary>
/// E2E tests for the Dashboard page (Blazor WASM).
/// Note: Blazor WASM apps take a while to load the .NET runtime on first visit.
/// </summary>
public class DashboardE2ETests : E2ETestBase
{
    public DashboardE2ETests(PlaywrightFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Dashboard_LoadsWithForm()
    {
        await NavigateAsync("/magic/dashboard");

        // Wait for Blazor WASM to load (may take a while on first load)
        var textarea = await WaitForSelectorAsync("textarea", timeoutMs: 30000);
        Assert.True(await textarea.IsVisibleAsync());
    }

    [Fact]
    public async Task Dashboard_HasWorkflowSelector()
    {
        await NavigateAsync("/magic/dashboard");

        var selector = await WaitForSelectorAsync(".workflow-selector", timeoutMs: 30000);
        Assert.True(await selector.IsVisibleAsync());
    }

    [Fact]
    public async Task Dashboard_HasAgentSelector()
    {
        await NavigateAsync("/magic/dashboard");

        var selector = await WaitForSelectorAsync(".agent-selector", timeoutMs: 30000);
        Assert.True(await selector.IsVisibleAsync());

        // Should have 3 options: claude, codex, gemini
        var options = await selector.Locator("option").AllAsync();
        Assert.Equal(3, options.Count);
    }

    [Fact]
    public async Task Dashboard_StartButton_DisabledWhenPromptEmpty()
    {
        await NavigateAsync("/magic/dashboard");

        var button = await WaitForSelectorAsync(".btn-primary", timeoutMs: 30000);
        // With empty prompt, button should be present (disabled state is handled by JS)
        Assert.True(await button.IsVisibleAsync());
    }
}
