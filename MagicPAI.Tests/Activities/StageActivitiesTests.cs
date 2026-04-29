using FluentAssertions;
using MagicPAI.Activities.Contracts;
using MagicPAI.Activities.Stage;
using MagicPAI.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Temporalio.Testing;

namespace MagicPAI.Tests.Activities;

/// <summary>
/// Unit tests for <see cref="StageActivities"/> — thin Temporal wrapper over
/// <see cref="ISessionStreamSink.EmitStageAsync"/>. Verifies the activity
/// forwards the SessionId + Stage to the sink and swallows sink failures so
/// stage emission can never fail a workflow.
/// </summary>
[Trait("Category", "Unit")]
public class StageActivitiesTests
{
    private static StageActivities BuildSut(Mock<ISessionStreamSink> sink) =>
        new(sink: sink.Object, log: NullLogger<StageActivities>.Instance);

    [Fact]
    public async Task EmitStageAsync_ForwardsSessionIdAndStage_ToSink()
    {
        var sink = new Mock<ISessionStreamSink>(MockBehavior.Strict);
        sink.Setup(s => s.EmitStageAsync(
                "sess-1", "architect", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = BuildSut(sink);
        var input = new EmitStageInput(SessionId: "sess-1", Stage: "architect");

        var env = new ActivityEnvironment();
        await env.RunAsync(() => sut.EmitStageAsync(input));

        sink.Verify(s => s.EmitStageAsync(
            "sess-1", "architect", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EmitStageAsync_SwallowsSinkException_SoWorkflowsKeepRunning()
    {
        var sink = new Mock<ISessionStreamSink>(MockBehavior.Loose);
        sink.Setup(s => s.EmitStageAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("hub down"));

        var sut = BuildSut(sink);
        var input = new EmitStageInput(SessionId: "sess-2", Stage: "workers");

        var env = new ActivityEnvironment();
        Func<Task> act = () => env.RunAsync(() => sut.EmitStageAsync(input));

        await act.Should().NotThrowAsync();
        sink.Verify(s => s.EmitStageAsync(
            "sess-2", "workers", It.IsAny<CancellationToken>()), Times.Once);
    }
}
