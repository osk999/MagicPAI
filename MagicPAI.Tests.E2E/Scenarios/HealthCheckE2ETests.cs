using MagicPAI.Tests.E2E.Fixtures;

namespace MagicPAI.Tests.E2E.Scenarios;

/// <summary>
/// Basic E2E tests that verify the server is running and health endpoints work.
/// These are the simplest E2E tests and serve as a smoke test.
/// </summary>
public class HealthCheckE2ETests : E2ETestBase
{
    public HealthCheckE2ETests(PlaywrightFixture fixture) : base(fixture) { }

    [Fact]
    public async Task HealthEndpoint_Returns200()
    {
        var response = await Page.GotoAsync($"{BaseUrl}/health");
        Assert.NotNull(response);
        Assert.True(response.Ok, $"Health check returned {response.Status}");
    }

    [Fact]
    public async Task ApiWorkflows_ReturnsJson()
    {
        var response = await Page.GotoAsync($"{BaseUrl}/api/browse/workflows");
        Assert.NotNull(response);
        Assert.True(response.Ok);
        var text = await response.TextAsync();
        Assert.Contains("full-orchestrate", text);
    }

    [Fact]
    public async Task ApiSessions_ReturnsJson()
    {
        var response = await Page.GotoAsync($"{BaseUrl}/api/sessions");
        Assert.NotNull(response);
        Assert.True(response.Ok);
    }
}
