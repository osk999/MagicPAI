using Microsoft.Playwright;

namespace MagicPAI.Tests.E2E.Fixtures;

/// <summary>
/// Shared fixture that manages a Playwright browser instance.
/// The server is expected to be running (via docker-compose.test.yml or manually).
/// Set BASE_URL environment variable to override the default.
/// </summary>
public class PlaywrightFixture : IAsyncLifetime
{
    public IPlaywright Playwright { get; private set; } = null!;
    public IBrowser Browser { get; private set; } = null!;
    public string BaseUrl { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        BaseUrl = Environment.GetEnvironmentVariable("BASE_URL") ?? "http://localhost:5000";

        Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        Browser = await Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });

        // Wait for server to be ready
        using var httpClient = new HttpClient();
        var maxRetries = 30;
        for (var i = 0; i < maxRetries; i++)
        {
            try
            {
                var response = await httpClient.GetAsync($"{BaseUrl}/health");
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch
            {
                // Server not ready yet
            }
            await Task.Delay(1000);
        }

        throw new TimeoutException(
            $"Server at {BaseUrl} did not become healthy within {maxRetries} seconds");
    }

    public async Task DisposeAsync()
    {
        await Browser.DisposeAsync();
        Playwright.Dispose();
    }

    public async Task<IPage> NewPageAsync()
    {
        var context = await Browser.NewContextAsync();
        return await context.NewPageAsync();
    }
}
