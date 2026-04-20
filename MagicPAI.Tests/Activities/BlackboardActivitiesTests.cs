using FluentAssertions;
using MagicPAI.Activities.Contracts;
using MagicPAI.Activities.Infrastructure;
using MagicPAI.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Temporalio.Testing;

namespace MagicPAI.Tests.Activities;

/// <summary>
/// Unit tests for <see cref="BlackboardActivities"/> — thin Temporal wrapper
/// around <see cref="SharedBlackboard"/>. Uses a real blackboard (it's a plain
/// in-memory <c>ConcurrentDictionary</c>) so we verify the real atomicity
/// behavior rather than mocking it away. See temporal.md §I.4.
/// </summary>
[Trait("Category", "Unit")]
public class BlackboardActivitiesTests
{
    private static BlackboardActivities BuildSut(SharedBlackboard? blackboard = null)
    {
        blackboard ??= new SharedBlackboard();
        return new BlackboardActivities(
            blackboard: blackboard,
            log: NullLogger<BlackboardActivities>.Instance);
    }

    // ── ClaimFileAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task ClaimFileAsync_SucceedsAndReturnsClaimed_WhenFileUnowned()
    {
        var blackboard = new SharedBlackboard();
        var sut = BuildSut(blackboard);
        var input = new ClaimFileInput(
            FilePath: "src/Login.razor",
            TaskId: "task-1",
            SessionId: "sess-1");

        var env = new ActivityEnvironment();
        var output = await env.RunAsync(() => sut.ClaimFileAsync(input));

        output.Claimed.Should().BeTrue();
        output.CurrentOwner.Should().BeNull();
        blackboard.GetFileOwner("src/Login.razor").Should().Be("task-1");
    }

    [Fact]
    public async Task ClaimFileAsync_ReturnsCurrentOwner_WhenFileAlreadyClaimed()
    {
        // Arrange — task-1 already owns the file.
        var blackboard = new SharedBlackboard();
        blackboard.ClaimFile("src/Login.razor", "task-1").Should().BeTrue();

        var sut = BuildSut(blackboard);
        var input = new ClaimFileInput(
            FilePath: "src/Login.razor",
            TaskId: "task-2",              // different task now tries to claim
            SessionId: "sess-1");

        // Act
        var env = new ActivityEnvironment();
        var output = await env.RunAsync(() => sut.ClaimFileAsync(input));

        // Assert
        output.Claimed.Should().BeFalse();
        output.CurrentOwner.Should().Be("task-1");
        blackboard.GetFileOwner("src/Login.razor").Should().Be("task-1");  // unchanged
    }

    [Fact]
    public async Task ClaimFileAsync_IsIdempotent_ForSameOwner()
    {
        var blackboard = new SharedBlackboard();
        blackboard.ClaimFile("src/Foo.cs", "task-7").Should().BeTrue();

        var sut = BuildSut(blackboard);
        var input = new ClaimFileInput(
            FilePath: "src/Foo.cs",
            TaskId: "task-7",              // same task re-claims
            SessionId: "sess-1");

        var env = new ActivityEnvironment();
        var output = await env.RunAsync(() => sut.ClaimFileAsync(input));

        output.Claimed.Should().BeTrue();
        output.CurrentOwner.Should().BeNull();
    }

    // ── ReleaseFileAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task ReleaseFileAsync_RemovesClaim_WhenTaskOwnsFile()
    {
        // Arrange — task-1 owns the file.
        var blackboard = new SharedBlackboard();
        blackboard.ClaimFile("src/Login.razor", "task-1").Should().BeTrue();

        var sut = BuildSut(blackboard);
        var input = new ReleaseFileInput(
            FilePath: "src/Login.razor",
            TaskId: "task-1",
            SessionId: "sess-1");

        // Act
        var env = new ActivityEnvironment();
        await env.RunAsync(() => sut.ReleaseFileAsync(input));

        // Assert
        blackboard.GetFileOwner("src/Login.razor").Should().BeNull();
    }

    [Fact]
    public async Task ReleaseFileAsync_IsNoOp_WhenTaskIsNotOwner()
    {
        var blackboard = new SharedBlackboard();
        blackboard.ClaimFile("src/Login.razor", "task-1").Should().BeTrue();

        var sut = BuildSut(blackboard);
        var input = new ReleaseFileInput(
            FilePath: "src/Login.razor",
            TaskId: "task-2",  // not the owner
            SessionId: "sess-1");

        var env = new ActivityEnvironment();
        await env.RunAsync(() => sut.ReleaseFileAsync(input));

        // Owner should be unchanged
        blackboard.GetFileOwner("src/Login.razor").Should().Be("task-1");
    }
}
