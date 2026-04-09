using MagicPAI.Core.Models;
using MagicPAI.Server.Bridge;
using MagicPAI.Server.Hubs;
using MagicPAI.Tests.Integration.Fixtures;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace MagicPAI.Tests.Integration.Hub;

public class SessionHubIntegrationTests : IntegrationTestBase, IAsyncLifetime
{
    private HubConnection? _hubConnection;

    public SessionHubIntegrationTests(MagicPaiWebApplicationFactory factory) : base(factory) { }

    public async Task InitializeAsync()
    {
        var server = Factory.Server;
        _hubConnection = new HubConnectionBuilder()
            .WithUrl(
                new Uri(server.BaseAddress, "/hub"),
                options => options.HttpMessageHandlerFactory = _ => server.CreateHandler())
            .Build();

        await _hubConnection.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_hubConnection is not null)
            await _hubConnection.DisposeAsync();
    }

    [Fact]
    public async Task ListSessions_ReturnsCollection()
    {
        var sessions = await _hubConnection!.InvokeAsync<IReadOnlyCollection<SessionInfo>>("ListSessions");
        Assert.NotNull(sessions);
    }

    [Fact]
    public async Task GetSessionOutput_UnknownSession_ReturnsEmpty()
    {
        var output = await _hubConnection!.InvokeAsync<string[]>("GetSessionOutput", "nonexistent");
        Assert.NotNull(output);
        Assert.Empty(output);
    }

    [Fact]
    public async Task GetSession_UnknownSession_ReturnsNull()
    {
        var session = await _hubConnection!.InvokeAsync<SessionInfo?>("GetSession", "nonexistent");
        Assert.Null(session);
    }

    [Fact]
    public async Task JoinAndLeaveSession_DoesNotThrow()
    {
        await _hubConnection!.InvokeAsync("JoinSession", "some-session-id");
        await _hubConnection!.InvokeAsync("LeaveSession", "some-session-id");
    }

    [Fact]
    public async Task GetSessionOutput_AfterAppendingOutput_ReturnsBuffered()
    {
        // Pre-populate tracker with data
        using var scope = Factory.Services.CreateScope();
        var tracker = scope.ServiceProvider.GetRequiredService<SessionTracker>();
        tracker.RegisterSession("hub-output-test", new SessionInfo { Id = "hub-output-test", State = "running" });
        tracker.AppendOutput("hub-output-test", "hello from hub");

        var output = await _hubConnection!.InvokeAsync<string[]>("GetSessionOutput", "hub-output-test");
        Assert.NotNull(output);
        Assert.Contains("hello from hub", output);
    }

    [Fact]
    public async Task GetSession_RegisteredSession_ReturnsInfo()
    {
        using var scope = Factory.Services.CreateScope();
        var tracker = scope.ServiceProvider.GetRequiredService<SessionTracker>();
        tracker.RegisterSession("hub-info-test", new SessionInfo
        {
            Id = "hub-info-test",
            State = "running",
            Agent = "claude"
        });

        var session = await _hubConnection!.InvokeAsync<SessionInfo?>("GetSession", "hub-info-test");
        Assert.NotNull(session);
        Assert.Equal("hub-info-test", session.Id);
        Assert.Equal("running", session.State);
    }
}
