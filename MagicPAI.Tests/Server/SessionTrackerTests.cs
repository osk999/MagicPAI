using MagicPAI.Core.Models;
using MagicPAI.Server.Bridge;

namespace MagicPAI.Tests.Server;

public class SessionTrackerTests
{
    private readonly SessionTracker _tracker = new(maxBufferSize: 5);

    // --- RegisterSession ---

    [Fact]
    public void RegisterSession_AddsSessionToCollection()
    {
        var info = new SessionInfo { Id = "s1", State = "running" };

        _tracker.RegisterSession("s1", info);

        var result = _tracker.GetSession("s1");
        Assert.NotNull(result);
        Assert.Equal("running", result.State);
    }

    [Fact]
    public void RegisterSession_InitializesOutputBuffer()
    {
        _tracker.RegisterSession("s1", new SessionInfo { Id = "s1" });

        var output = _tracker.GetOutput("s1");
        Assert.Empty(output);
    }

    [Fact]
    public void RegisterSession_InitializesActivityStates()
    {
        _tracker.RegisterSession("s1", new SessionInfo { Id = "s1" });

        var activities = _tracker.GetActivities("s1");
        Assert.Empty(activities);
    }

    // --- UpdateState ---

    [Fact]
    public void UpdateState_ChangesExistingSessionState()
    {
        _tracker.RegisterSession("s1", new SessionInfo { Id = "s1", State = "running" });

        _tracker.UpdateState("s1", "completed");

        Assert.Equal("completed", _tracker.GetSession("s1")!.State);
    }

    [Fact]
    public void UpdateState_PreservesOtherFields()
    {
        _tracker.RegisterSession("s1", new SessionInfo
        {
            Id = "s1",
            State = "running",
            Agent = "claude",
            WorkflowId = "full-orchestrate",
            PromptPreview = "Fix bug"
        });

        _tracker.UpdateState("s1", "completed");

        var session = _tracker.GetSession("s1")!;
        Assert.Equal("claude", session.Agent);
        Assert.Equal("full-orchestrate", session.WorkflowId);
        Assert.Equal("Fix bug", session.PromptPreview);
    }

    [Fact]
    public void UpdateState_CreatesNewEntryIfNotExists()
    {
        _tracker.UpdateState("s-new", "running");

        var session = _tracker.GetSession("s-new");
        Assert.NotNull(session);
        Assert.Equal("running", session.State);
        Assert.Equal("s-new", session.Id);
    }

    // --- UpdateContainer ---

    [Fact]
    public void UpdateContainer_SetsContainerId()
    {
        _tracker.RegisterSession("s1", new SessionInfo { Id = "s1" });

        _tracker.UpdateContainer("s1", "ctr-abc");

        Assert.Equal("ctr-abc", _tracker.GetSession("s1")!.ContainerId);
    }

    [Fact]
    public void UpdateContainer_ClearsContainerIdWhenNull()
    {
        _tracker.RegisterSession("s1", new SessionInfo { Id = "s1", ContainerId = "ctr-abc" });

        _tracker.UpdateContainer("s1", null);

        Assert.Null(_tracker.GetSession("s1")!.ContainerId);
    }

    // --- AppendOutput / GetOutput ---

    [Fact]
    public void AppendOutput_EnqueuesText()
    {
        _tracker.RegisterSession("s1", new SessionInfo { Id = "s1" });

        _tracker.AppendOutput("s1", "line1");
        _tracker.AppendOutput("s1", "line2");

        var output = _tracker.GetOutput("s1");
        Assert.Equal(["line1", "line2"], output);
    }

    [Fact]
    public void AppendOutput_TrimsAtMaxBufferSize()
    {
        // maxBufferSize = 5 in this test class
        _tracker.RegisterSession("s1", new SessionInfo { Id = "s1" });

        for (int i = 0; i < 8; i++)
            _tracker.AppendOutput("s1", $"line{i}");

        var output = _tracker.GetOutput("s1");
        Assert.Equal(5, output.Length);
        Assert.Equal("line3", output[0]);
        Assert.Equal("line7", output[4]);
    }

    [Fact]
    public void AppendOutput_CreatesBufferIfNotRegistered()
    {
        _tracker.AppendOutput("unregistered", "text");

        var output = _tracker.GetOutput("unregistered");
        Assert.Equal(["text"], output);
    }

    [Fact]
    public void GetOutput_ReturnsEmptyForUnknownSession()
    {
        var output = _tracker.GetOutput("nonexistent");
        Assert.Empty(output);
    }

    // --- UpdateActivity / GetActivities ---

    [Fact]
    public void UpdateActivity_TracksActivityState()
    {
        _tracker.RegisterSession("s1", new SessionInfo { Id = "s1" });

        _tracker.UpdateActivity("s1", "RunCliAgent", "running");

        var activities = _tracker.GetActivities("s1");
        Assert.Single(activities);
        Assert.Equal("RunCliAgent", activities[0].Name);
        Assert.Equal("running", activities[0].Status);
    }

    [Fact]
    public void UpdateActivity_OverwritesPreviousStatus()
    {
        _tracker.RegisterSession("s1", new SessionInfo { Id = "s1" });

        _tracker.UpdateActivity("s1", "RunCliAgent", "running");
        _tracker.UpdateActivity("s1", "RunCliAgent", "completed");

        var activities = _tracker.GetActivities("s1");
        Assert.Single(activities);
        Assert.Equal("completed", activities[0].Status);
    }

    [Fact]
    public void GetActivities_ReturnsSortedByName()
    {
        _tracker.RegisterSession("s1", new SessionInfo { Id = "s1" });

        _tracker.UpdateActivity("s1", "Zeta", "running");
        _tracker.UpdateActivity("s1", "Alpha", "completed");
        _tracker.UpdateActivity("s1", "Middle", "failed");

        var activities = _tracker.GetActivities("s1");
        Assert.Equal("Alpha", activities[0].Name);
        Assert.Equal("Middle", activities[1].Name);
        Assert.Equal("Zeta", activities[2].Name);
    }

    [Fact]
    public void GetActivities_ReturnsEmptyForUnknownSession()
    {
        var activities = _tracker.GetActivities("nonexistent");
        Assert.Empty(activities);
    }

    // --- GetSession / GetAllSessions ---

    [Fact]
    public void GetSession_ReturnsNullForUnknown()
    {
        Assert.Null(_tracker.GetSession("nope"));
    }

    [Fact]
    public void GetAllSessions_ReturnsAllRegistered()
    {
        _tracker.RegisterSession("s1", new SessionInfo { Id = "s1" });
        _tracker.RegisterSession("s2", new SessionInfo { Id = "s2" });

        var all = _tracker.GetAllSessions();
        Assert.Equal(2, all.Count);
    }

    // --- RemoveSession ---

    [Fact]
    public void RemoveSession_CleansAllData()
    {
        _tracker.RegisterSession("s1", new SessionInfo { Id = "s1" });
        _tracker.AppendOutput("s1", "text");
        _tracker.UpdateActivity("s1", "Act1", "running");

        _tracker.RemoveSession("s1");

        Assert.Null(_tracker.GetSession("s1"));
        Assert.Empty(_tracker.GetOutput("s1"));
        Assert.Empty(_tracker.GetActivities("s1"));
    }

    [Fact]
    public void RemoveSession_NoOpForUnknown()
    {
        // Should not throw
        _tracker.RemoveSession("nonexistent");
    }

    // --- Thread safety ---

    [Fact]
    public async Task ConcurrentAppendOutput_IsThreadSafe()
    {
        var tracker = new SessionTracker(maxBufferSize: 10000);
        tracker.RegisterSession("s1", new SessionInfo { Id = "s1" });

        var tasks = Enumerable.Range(0, 100)
            .Select(i => Task.Run(() => tracker.AppendOutput("s1", $"line{i}")));

        await Task.WhenAll(tasks);

        var output = tracker.GetOutput("s1");
        Assert.Equal(100, output.Length);
    }

    [Fact]
    public async Task ConcurrentUpdateState_IsThreadSafe()
    {
        var tracker = new SessionTracker();
        tracker.RegisterSession("s1", new SessionInfo { Id = "s1", State = "running" });

        var tasks = Enumerable.Range(0, 50)
            .Select(i => Task.Run(() => tracker.UpdateState("s1", i % 2 == 0 ? "running" : "completed")));

        await Task.WhenAll(tasks);

        var session = tracker.GetSession("s1");
        Assert.NotNull(session);
        Assert.Contains(session.State, new[] { "running", "completed" });
    }
}
