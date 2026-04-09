using System.Net;
using System.Net.Http.Json;
using MagicPAI.Core.Models;
using MagicPAI.Server.Bridge;
using MagicPAI.Server.Controllers;
using MagicPAI.Tests.Integration.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace MagicPAI.Tests.Integration.Api;

public class SessionControllerIntegrationTests : IntegrationTestBase
{
    public SessionControllerIntegrationTests(MagicPaiWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task CreateSession_EmptyPrompt_Returns400()
    {
        var request = new CreateSessionRequest("");
        var response = await Client.PostAsJsonAsync("/api/sessions", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ListSessions_ReturnsCollection()
    {
        var response = await Client.GetAsync("/api/sessions");
        response.EnsureSuccessStatusCode();

        var sessions = await response.Content.ReadFromJsonAsync<List<SessionInfo>>();
        Assert.NotNull(sessions);
    }

    [Fact]
    public async Task GetSession_UnknownId_Returns404()
    {
        var response = await Client.GetAsync("/api/sessions/nonexistent-id");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task StopSession_UnknownId_Returns404()
    {
        var response = await Client.DeleteAsync("/api/sessions/nonexistent-id");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ApproveSession_UnknownId_Returns404()
    {
        var request = new ApprovalRequest(true);
        var response = await Client.PostAsJsonAsync("/api/sessions/nonexistent-id/approve", request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetOutput_UnknownId_Returns404()
    {
        var response = await Client.GetAsync("/api/sessions/nonexistent-id/output");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetActivities_UnknownId_Returns404()
    {
        var response = await Client.GetAsync("/api/sessions/nonexistent-id/activities");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateSession_RegistersInTracker()
    {
        // Pre-register a session in the tracker to test ListSessions
        using var scope = Factory.Services.CreateScope();
        var tracker = scope.ServiceProvider.GetRequiredService<SessionTracker>();
        tracker.RegisterSession("test-tracker-session", new SessionInfo
        {
            Id = "test-tracker-session",
            State = "running",
            Agent = "claude"
        });

        var response = await Client.GetAsync("/api/sessions/test-tracker-session");
        response.EnsureSuccessStatusCode();

        var session = await response.Content.ReadFromJsonAsync<SessionInfo>();
        Assert.NotNull(session);
        Assert.Equal("test-tracker-session", session.Id);
    }

    [Fact]
    public async Task GetOutput_RegisteredSession_ReturnsBuffered()
    {
        using var scope = Factory.Services.CreateScope();
        var tracker = scope.ServiceProvider.GetRequiredService<SessionTracker>();

        tracker.RegisterSession("output-session", new SessionInfo { Id = "output-session", State = "running" });
        tracker.AppendOutput("output-session", "chunk1");
        tracker.AppendOutput("output-session", "chunk2");

        var response = await Client.GetAsync("/api/sessions/output-session/output");
        response.EnsureSuccessStatusCode();

        var output = await response.Content.ReadFromJsonAsync<string[]>();
        Assert.NotNull(output);
        Assert.Contains("chunk1", output);
        Assert.Contains("chunk2", output);
    }

    [Fact]
    public async Task GetActivities_RegisteredSession_ReturnsActivities()
    {
        using var scope = Factory.Services.CreateScope();
        var tracker = scope.ServiceProvider.GetRequiredService<SessionTracker>();

        tracker.RegisterSession("act-session", new SessionInfo { Id = "act-session", State = "running" });
        tracker.UpdateActivity("act-session", "SpawnContainer", "completed");
        tracker.UpdateActivity("act-session", "RunCliAgent", "running");

        var response = await Client.GetAsync("/api/sessions/act-session/activities");
        response.EnsureSuccessStatusCode();

        var activities = await response.Content.ReadFromJsonAsync<List<ActivityState>>();
        Assert.NotNull(activities);
        Assert.Equal(2, activities.Count);
    }
}
