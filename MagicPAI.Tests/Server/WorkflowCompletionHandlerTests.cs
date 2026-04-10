using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Notifications;
using Elsa.Workflows.State;
using MagicPAI.Core.Models;
using MagicPAI.Core.Services;
using MagicPAI.Server.Bridge;
using MagicPAI.Server.Hubs;
using MagicPAI.Server.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace MagicPAI.Tests.Server;

public class WorkflowCompletionHandlerTests
{
    private readonly Mock<IHubContext<SessionHub>> _hubContextMock;
    private readonly Mock<IClientProxy> _clientProxyMock;
    private readonly Mock<IContainerManager> _containerManagerMock;
    private readonly SessionTracker _tracker;
    private readonly WorkflowCompletionHandler _handler;
    private readonly List<(string Method, object?[] Args)> _sentMessages = [];

    public WorkflowCompletionHandlerTests()
    {
        _hubContextMock = new Mock<IHubContext<SessionHub>>();
        _clientProxyMock = new Mock<IClientProxy>();
        _containerManagerMock = new Mock<IContainerManager>();
        _tracker = new SessionTracker();

        _clientProxyMock
            .Setup(c => c.SendCoreAsync(
                It.IsAny<string>(),
                It.IsAny<object?[]>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, object?[], CancellationToken>((method, args, _) =>
            {
                _sentMessages.Add((method, args));
            })
            .Returns(Task.CompletedTask);

        var mockClients = new Mock<IHubClients>();
        mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(_clientProxyMock.Object);
        _hubContextMock.Setup(h => h.Clients).Returns(mockClients.Object);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var logStreamer = new SessionContainerLogStreamer(
            _containerManagerMock.Object,
            _tracker,
            _hubContextMock.Object,
            NullLogger<SessionContainerLogStreamer>.Instance);

        _handler = new WorkflowCompletionHandler(
            _hubContextMock.Object,
            _tracker,
            _containerManagerMock.Object,
            null,
            logStreamer,
            configuration,
            NullLogger<WorkflowCompletionHandler>.Instance);
    }

    [Fact]
    public async Task HandleAsync_UnknownSession_DoesNotSendEvents()
    {
        var notification = CreateNotification("unknown-id", WorkflowStatus.Finished, WorkflowSubStatus.Finished);

        await _handler.HandleAsync(notification, CancellationToken.None);

        Assert.Empty(_sentMessages);
    }

    [Fact]
    public async Task HandleAsync_FinishedFinished_SetsCompleted()
    {
        _tracker.RegisterSession("s1", new SessionInfo { Id = "s1", State = "running" });
        var notification = CreateNotification("s1", WorkflowStatus.Finished, WorkflowSubStatus.Finished);

        await _handler.HandleAsync(notification, CancellationToken.None);

        Assert.Equal("completed", _tracker.GetSession("s1")!.State);
        Assert.Single(_sentMessages);
        Assert.Equal("sessionStateChanged", _sentMessages[0].Method);
    }

    [Fact]
    public async Task HandleAsync_FinishedFaulted_SetsFailed()
    {
        _tracker.RegisterSession("s1", new SessionInfo { Id = "s1", State = "running" });
        var notification = CreateNotification("s1", WorkflowStatus.Finished, WorkflowSubStatus.Faulted);

        await _handler.HandleAsync(notification, CancellationToken.None);

        Assert.Equal("failed", _tracker.GetSession("s1")!.State);
        Assert.Single(_sentMessages);
        Assert.Equal("sessionStateChanged", _sentMessages[0].Method);
    }

    [Fact]
    public async Task HandleAsync_FinishedCancelled_SetsCancelled()
    {
        _tracker.RegisterSession("s1", new SessionInfo { Id = "s1", State = "running" });
        var notification = CreateNotification("s1", WorkflowStatus.Finished, WorkflowSubStatus.Cancelled);

        await _handler.HandleAsync(notification, CancellationToken.None);

        Assert.Equal("cancelled", _tracker.GetSession("s1")!.State);
    }

    [Fact]
    public async Task HandleAsync_Running_DoesNotUpdateState()
    {
        _tracker.RegisterSession("s1", new SessionInfo { Id = "s1", State = "running" });
        var notification = CreateNotification("s1", WorkflowStatus.Running, WorkflowSubStatus.Executing);

        await _handler.HandleAsync(notification, CancellationToken.None);

        // "running" maps to newState "running", which is not in the terminal set
        Assert.Equal("running", _tracker.GetSession("s1")!.State);
        Assert.Empty(_sentMessages); // No sessionStateChanged for non-terminal
    }

    private static WorkflowExecuted CreateNotification(
        string instanceId,
        WorkflowStatus status,
        WorkflowSubStatus subStatus)
    {
        var workflowState = new WorkflowState
        {
            Id = instanceId,
            Status = status,
            SubStatus = subStatus
        };

        // WorkflowExecuted requires (Workflow, WorkflowState, WorkflowExecutionContext)
        // The handler only reads notification.WorkflowState, so we pass null for context.
        return new WorkflowExecuted(new Workflow(), workflowState, null!);
    }
}
