using Elsa.Workflows;
using Elsa.Workflows.Runtime.Entities;
using Elsa.Workflows.Runtime.Notifications;
using MagicPAI.Core.Models;
using MagicPAI.Server.Bridge;
using MagicPAI.Server.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace MagicPAI.Tests.Server;

public class WorkflowProgressTrackerTests
{
    private readonly Mock<IHubContext<SessionHub>> _hubContextMock;
    private readonly Mock<IClientProxy> _clientProxyMock;
    private readonly SessionTracker _tracker;
    private readonly WorkflowProgressTracker _progressTracker;
    private readonly List<(string Method, object?[] Args)> _sentMessages = [];

    public WorkflowProgressTrackerTests()
    {
        _hubContextMock = new Mock<IHubContext<SessionHub>>();
        _clientProxyMock = new Mock<IClientProxy>();
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

        _progressTracker = new WorkflowProgressTracker(
            _hubContextMock.Object,
            _tracker,
            NullLogger<WorkflowProgressTracker>.Instance);
    }

    [Fact]
    public async Task HandleAsync_UnknownSession_DoesNotSendEvents()
    {
        // Session not registered — should be a no-op
        var record = CreateRecord("unknown-session", "act1", ActivityStatus.Running);
        var notification = new ActivityExecutionRecordUpdated(record);

        await _progressTracker.HandleAsync(notification, CancellationToken.None);

        Assert.Empty(_sentMessages);
    }

    [Fact]
    public async Task HandleAsync_RunningActivity_SendsWorkflowProgress()
    {
        _tracker.RegisterSession("s1", new SessionInfo { Id = "s1", State = "running" });
        var record = CreateRecord("s1", "RunCliAgent", ActivityStatus.Running);
        var notification = new ActivityExecutionRecordUpdated(record);

        await _progressTracker.HandleAsync(notification, CancellationToken.None);

        Assert.Single(_sentMessages);
        Assert.Equal("workflowProgress", _sentMessages[0].Method);
    }

    [Fact]
    public async Task HandleAsync_CompletedActivity_IncrementsCount()
    {
        _tracker.RegisterSession("s1", new SessionInfo { Id = "s1", State = "running" });

        var record1 = CreateRecord("s1", "Act1", ActivityStatus.Completed);
        await _progressTracker.HandleAsync(new ActivityExecutionRecordUpdated(record1), CancellationToken.None);

        var record2 = CreateRecord("s1", "Act2", ActivityStatus.Completed);
        await _progressTracker.HandleAsync(new ActivityExecutionRecordUpdated(record2), CancellationToken.None);

        // Both messages should be workflowProgress
        Assert.Equal(2, _sentMessages.Count);
        Assert.All(_sentMessages, m => Assert.Equal("workflowProgress", m.Method));
    }

    [Fact]
    public async Task HandleAsync_FaultedActivity_UpdatesSessionStateFailed()
    {
        _tracker.RegisterSession("s1", new SessionInfo { Id = "s1", State = "running" });
        var record = CreateRecord("s1", "RunCliAgent", ActivityStatus.Faulted);
        var notification = new ActivityExecutionRecordUpdated(record);

        await _progressTracker.HandleAsync(notification, CancellationToken.None);

        // Should send both workflowProgress and sessionStateChanged
        Assert.Equal(2, _sentMessages.Count);
        Assert.Equal("workflowProgress", _sentMessages[0].Method);
        Assert.Equal("sessionStateChanged", _sentMessages[1].Method);

        // Session state should be updated in tracker
        Assert.Equal("failed", _tracker.GetSession("s1")!.State);
    }

    [Fact]
    public async Task HandleAsync_TracksActivityInTracker()
    {
        _tracker.RegisterSession("s1", new SessionInfo { Id = "s1", State = "running" });
        var record = CreateRecord("s1", "SpawnContainer", ActivityStatus.Running);
        var notification = new ActivityExecutionRecordUpdated(record);

        await _progressTracker.HandleAsync(notification, CancellationToken.None);

        var activities = _tracker.GetActivities("s1");
        Assert.Single(activities);
        Assert.Equal("SpawnContainer", activities[0].Name);
        Assert.Equal("running", activities[0].Status);
    }

    private static ActivityExecutionRecord CreateRecord(
        string workflowInstanceId,
        string activityId,
        ActivityStatus status)
    {
        return new ActivityExecutionRecord
        {
            WorkflowInstanceId = workflowInstanceId,
            ActivityId = activityId,
            ActivityName = activityId,
            ActivityType = activityId,
            Status = status,
        };
    }
}
