using Microsoft.Playwright;

namespace MagicPAI.Tests.E2E.Fixtures;

/// <summary>
/// Base class for E2E tests. Provides a fresh browser page per test class.
/// </summary>
public abstract class E2ETestBase : IClassFixture<PlaywrightFixture>, IAsyncLifetime
{
    protected readonly PlaywrightFixture Fixture;
    protected IPage Page { get; private set; } = null!;
    protected string BaseUrl => Fixture.BaseUrl;

    protected E2ETestBase(PlaywrightFixture fixture)
    {
        Fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        Page = await Fixture.NewPageAsync();
    }

    public async Task DisposeAsync()
    {
        await Page.Context.DisposeAsync();
    }

    /// <summary>Navigate to a page and wait for network idle.</summary>
    protected async Task NavigateAsync(string path)
    {
        await Page.GotoAsync(
            $"{BaseUrl}{path}",
            new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 30000 });
    }

    /// <summary>Wait for a specific element to appear.</summary>
    protected async Task<ILocator> WaitForSelectorAsync(string selector, float timeoutMs = 10000)
    {
        var locator = Page.Locator(selector);
        await locator.WaitForAsync(new LocatorWaitForOptions { Timeout = timeoutMs });
        return locator;
    }
}
