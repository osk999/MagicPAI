using MagicPAI.Core.Config;
using MagicPAI.Core.Models;
using MagicPAI.Core.Services;
using MagicPAI.Server.Bridge;
using MagicPAI.Server.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace MagicPAI.Tests.Services;

/// <summary>
/// Unit tests for <see cref="WorkerPodGarbageCollector"/>.
///
/// Two sweeps under test:
///   1. Tracker sweep — destroys containers whose tracked session is terminal.
///   2. Label fallback sweep (BUG-4) — destroys containers carrying the
///      <c>magicpai.session</c> label whose owning session is unknown to the
///      tracker (server-restart orphan), only after a 5-minute grace period.
///
/// We drive the collector by calling the public <c>ScanAndCleanupAsync</c>
/// method directly so tests don't have to spin a real BackgroundService.
/// </summary>
public class WorkerPodGarbageCollectorTests
{
    private const string SessionLabel = WorkerPodGarbageCollector.SessionLabel;

    private static WorkerPodGarbageCollector BuildSut(
        Mock<IContainerManager> docker,
        SessionTracker tracker,
        MagicPaiConfig? config = null) =>
        new(
            containerManager: docker.Object,
            sessionTracker: tracker,
            config: config ?? new MagicPaiConfig { ExecutionBackend = "docker" },
            logger: NullLogger<WorkerPodGarbageCollector>.Instance);

    private static LabeledContainer Orphan(
        string containerId,
        string sessionId,
        DateTime createdAtUtc,
        bool isRunning = true) =>
        new(
            ContainerId: containerId,
            Labels: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [SessionLabel] = sessionId
            },
            CreatedAtUtc: createdAtUtc,
            IsRunning: isRunning);

    // -----------------------------------------------------------------------
    // Label fallback sweep: BUG-4 fix
    // -----------------------------------------------------------------------

    [Fact]
    public async Task LabelFallback_OldOrphan_UnknownToTracker_IsReaped()
    {
        // Tracker is empty (server restart scenario). Container is 39 minutes
        // old — well past the 5-minute grace period.
        var tracker = new SessionTracker();

        var docker = new Mock<IContainerManager>(MockBehavior.Strict);
        docker.Setup(d => d.ListContainersByLabelAsync(SessionLabel, It.IsAny<CancellationToken>()))
              .ReturnsAsync([
                  Orphan("ctr-busy-spence", "lost-session-99",
                      DateTime.UtcNow.AddMinutes(-39))
              ]);
        docker.Setup(d => d.DestroyAsync("ctr-busy-spence", It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);

        var sut = BuildSut(docker, tracker);

        await sut.ScanAndCleanupAsync(CancellationToken.None);

        docker.Verify(d => d.DestroyAsync("ctr-busy-spence", It.IsAny<CancellationToken>()),
            Times.Once,
            "Orphan whose session is unknown to the tracker must be reaped.");
    }

    [Fact]
    public async Task LabelFallback_OldOrphan_TerminalSession_StillRunning_IsReaped()
    {
        // Session is known but already terminal, yet the container is still
        // running — finally never got to destroy it. Reap.
        var tracker = new SessionTracker();
        tracker.RegisterSession("done-session-1",
            new MagicPAI.Shared.Models.SessionInfo { Id = "done-session-1", State = "completed" });

        var docker = new Mock<IContainerManager>(MockBehavior.Strict);
        docker.Setup(d => d.ListContainersByLabelAsync(SessionLabel, It.IsAny<CancellationToken>()))
              .ReturnsAsync([
                  Orphan("ctr-leaked", "done-session-1",
                      DateTime.UtcNow.AddMinutes(-30), isRunning: true)
              ]);
        docker.Setup(d => d.DestroyAsync("ctr-leaked", It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);

        // Tracker sweep also runs — but the session has no ContainerId set, so
        // it bails out immediately. Keep the strict mock happy.

        var sut = BuildSut(docker, tracker);
        await sut.ScanAndCleanupAsync(CancellationToken.None);

        docker.Verify(d => d.DestroyAsync("ctr-leaked", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task LabelFallback_FreshOrphan_WithinGracePeriod_IsNotReaped()
    {
        // Container is only 1 minute old — newly spawned, possibly not yet
        // registered with the tracker. Don't kill it.
        var tracker = new SessionTracker();

        var docker = new Mock<IContainerManager>(MockBehavior.Strict);
        docker.Setup(d => d.ListContainersByLabelAsync(SessionLabel, It.IsAny<CancellationToken>()))
              .ReturnsAsync([
                  Orphan("ctr-fresh", "racing-session",
                      DateTime.UtcNow.AddMinutes(-1))
              ]);

        var sut = BuildSut(docker, tracker);
        await sut.ScanAndCleanupAsync(CancellationToken.None);

        docker.Verify(d => d.DestroyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "Containers younger than the 5-minute grace period must not be touched.");
    }

    [Fact]
    public async Task LabelFallback_OldContainer_ActiveRunningSession_IsNotReaped()
    {
        // Session is active and non-terminal. Don't kill its container, even
        // if it's older than 5 minutes (legitimate long-running workflow).
        var tracker = new SessionTracker();
        tracker.RegisterSession("active-session-1",
            new MagicPAI.Shared.Models.SessionInfo
            {
                Id = "active-session-1",
                State = "running",
                ContainerId = "ctr-active"
            });

        var docker = new Mock<IContainerManager>(MockBehavior.Strict);
        docker.Setup(d => d.ListContainersByLabelAsync(SessionLabel, It.IsAny<CancellationToken>()))
              .ReturnsAsync([
                  Orphan("ctr-active", "active-session-1",
                      DateTime.UtcNow.AddMinutes(-20), isRunning: true)
              ]);

        var sut = BuildSut(docker, tracker);
        await sut.ScanAndCleanupAsync(CancellationToken.None);

        docker.Verify(d => d.DestroyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "An active, non-terminal session's container must never be reaped by the fallback sweep.");
    }

    [Fact]
    public async Task LabelFallback_OldContainer_TerminalSession_NotRunning_IsNotReaped()
    {
        // The tracker sweep has already destroyed the container; the engine
        // still lists its record as exited. Don't double-destroy.
        var tracker = new SessionTracker();
        tracker.RegisterSession("done-session-2",
            new MagicPAI.Shared.Models.SessionInfo { Id = "done-session-2", State = "completed" });

        var docker = new Mock<IContainerManager>(MockBehavior.Strict);
        docker.Setup(d => d.ListContainersByLabelAsync(SessionLabel, It.IsAny<CancellationToken>()))
              .ReturnsAsync([
                  Orphan("ctr-already-stopped", "done-session-2",
                      DateTime.UtcNow.AddMinutes(-10), isRunning: false)
              ]);

        var sut = BuildSut(docker, tracker);
        await sut.ScanAndCleanupAsync(CancellationToken.None);

        docker.Verify(d => d.DestroyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task LabelFallback_NoLabeledContainers_NoOp()
    {
        var tracker = new SessionTracker();

        var docker = new Mock<IContainerManager>(MockBehavior.Strict);
        docker.Setup(d => d.ListContainersByLabelAsync(SessionLabel, It.IsAny<CancellationToken>()))
              .ReturnsAsync(Array.Empty<LabeledContainer>());

        var sut = BuildSut(docker, tracker);
        await sut.ScanAndCleanupAsync(CancellationToken.None);

        docker.Verify(d => d.DestroyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task LabelFallback_DestroyFailure_DoesNotBreakLoop()
    {
        // First orphan throws on destroy; the second must still get reaped.
        var tracker = new SessionTracker();

        var docker = new Mock<IContainerManager>(MockBehavior.Strict);
        docker.Setup(d => d.ListContainersByLabelAsync(SessionLabel, It.IsAny<CancellationToken>()))
              .ReturnsAsync([
                  Orphan("ctr-bad",  "lost-1", DateTime.UtcNow.AddMinutes(-30)),
                  Orphan("ctr-good", "lost-2", DateTime.UtcNow.AddMinutes(-30))
              ]);
        docker.Setup(d => d.DestroyAsync("ctr-bad", It.IsAny<CancellationToken>()))
              .ThrowsAsync(new InvalidOperationException("docker daemon hiccup"));
        docker.Setup(d => d.DestroyAsync("ctr-good", It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);

        var sut = BuildSut(docker, tracker);
        await sut.ScanAndCleanupAsync(CancellationToken.None);

        docker.Verify(d => d.DestroyAsync("ctr-bad",  It.IsAny<CancellationToken>()), Times.Once);
        docker.Verify(d => d.DestroyAsync("ctr-good", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LabelFallback_ListThrows_DoesNotBreakLoop()
    {
        // Engine API can fail (Docker restarted, daemon stutter). The GC
        // must swallow the exception and complete cleanly so the next interval
        // gets another chance.
        var tracker = new SessionTracker();

        var docker = new Mock<IContainerManager>(MockBehavior.Strict);
        docker.Setup(d => d.ListContainersByLabelAsync(SessionLabel, It.IsAny<CancellationToken>()))
              .ThrowsAsync(new InvalidOperationException("docker daemon unreachable"));

        var sut = BuildSut(docker, tracker);

        var ex = await Record.ExceptionAsync(() => sut.ScanAndCleanupAsync(CancellationToken.None));
        Assert.Null(ex);

        docker.Verify(d => d.DestroyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // -----------------------------------------------------------------------
    // Tracker sweep: pre-existing behavior, regression-locked here.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task TrackerSweep_TerminalSession_RunningContainer_IsDestroyed()
    {
        var tracker = new SessionTracker();
        tracker.RegisterSession("term-1",
            new MagicPAI.Shared.Models.SessionInfo
            {
                Id = "term-1",
                State = "terminated",
                ContainerId = "ctr-term-1"
            });

        var docker = new Mock<IContainerManager>(MockBehavior.Strict);
        docker.Setup(d => d.IsRunningAsync("ctr-term-1", It.IsAny<CancellationToken>()))
              .ReturnsAsync(true);
        docker.Setup(d => d.DestroyAsync("ctr-term-1", It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);
        docker.Setup(d => d.ListContainersByLabelAsync(SessionLabel, It.IsAny<CancellationToken>()))
              .ReturnsAsync(Array.Empty<LabeledContainer>());

        var sut = BuildSut(docker, tracker);
        await sut.ScanAndCleanupAsync(CancellationToken.None);

        docker.Verify(d => d.DestroyAsync("ctr-term-1", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task TrackerSweep_RunningSession_IsNotTouched()
    {
        var tracker = new SessionTracker();
        tracker.RegisterSession("run-1",
            new MagicPAI.Shared.Models.SessionInfo
            {
                Id = "run-1",
                State = "running",
                ContainerId = "ctr-run-1"
            });

        var docker = new Mock<IContainerManager>(MockBehavior.Strict);
        docker.Setup(d => d.ListContainersByLabelAsync(SessionLabel, It.IsAny<CancellationToken>()))
              .ReturnsAsync(Array.Empty<LabeledContainer>());

        var sut = BuildSut(docker, tracker);
        await sut.ScanAndCleanupAsync(CancellationToken.None);

        docker.Verify(d => d.DestroyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        docker.Verify(d => d.IsRunningAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
