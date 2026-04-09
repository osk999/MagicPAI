using MagicPAI.Tests.E2E.Fixtures;

namespace MagicPAI.Tests.E2E.Scenarios;

/// <summary>
/// E2E tests for session creation and session view page.
/// These tests create sessions via the REST API and verify the UI reflects state.
/// </summary>
public class SessionE2ETests : E2ETestBase
{
    public SessionE2ETests(PlaywrightFixture fixture) : base(fixture) { }

    [Fact]
    public async Task CreateSession_ViaApi_AppearsInDashboard()
    {
        // Create a session via REST API
        var response = await Page.APIRequest.PostAsync(
            $"{BaseUrl}/api/sessions",
            new() { DataObject = new { Prompt = "E2E test task", WorkspacePath = "/tmp" } });

        Assert.True(response.Ok, $"Create session returned {response.Status}");

        // Navigate to dashboard and verify session appears
        await NavigateAsync("/magic/dashboard");
        await WaitForSelectorAsync(".session-card", timeoutMs: 30000);
    }

    [Fact]
    public async Task SessionView_LoadsForValidSession()
    {
        // Create a session
        var response = await Page.APIRequest.PostAsync(
            $"{BaseUrl}/api/sessions",
            new() { DataObject = new { Prompt = "E2E session view test", WorkspacePath = "/tmp" } });

        var json = await response.JsonAsync();
        var sessionId = json?.GetProperty("sessionId").GetString();
        Assert.NotNull(sessionId);

        // Navigate to session view
        await NavigateAsync($"/magic/sessions/{sessionId}");

        // Wait for the session view to load
        var outputPanel = await WaitForSelectorAsync(".output-panel", timeoutMs: 30000);
        Assert.True(await outputPanel.IsVisibleAsync());
    }

    [Fact]
    public async Task SessionView_ShowsStatusBadge()
    {
        // Create a session
        var response = await Page.APIRequest.PostAsync(
            $"{BaseUrl}/api/sessions",
            new() { DataObject = new { Prompt = "Status badge test", WorkspacePath = "/tmp" } });

        var json = await response.JsonAsync();
        var sessionId = json?.GetProperty("sessionId").GetString();
        Assert.NotNull(sessionId);

        await NavigateAsync($"/magic/sessions/{sessionId}");

        // Should show a status badge
        var badge = await WaitForSelectorAsync(".status-badge", timeoutMs: 30000);
        Assert.True(await badge.IsVisibleAsync());
    }
}
