using FluentAssertions;
using MagicPAI.Activities.Contracts;
using MagicPAI.Activities.Git;
using MagicPAI.Core.Models;
using MagicPAI.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Temporalio.Exceptions;
using Temporalio.Testing;

namespace MagicPAI.Tests.Activities;

/// <summary>
/// Unit tests for <see cref="GitActivities"/> — git worktree Temporal activity group.
/// All git commands are mocked via <see cref="IContainerManager.ExecAsync(string, string, string, CancellationToken)"/>.
/// See temporal.md §I.2.
/// </summary>
[Trait("Category", "Unit")]
public class GitActivitiesTests
{
    private static GitActivities BuildSut(Mock<IContainerManager>? docker = null)
    {
        docker ??= new Mock<IContainerManager>(MockBehavior.Loose);
        return new GitActivities(
            docker: docker.Object,
            log: NullLogger<GitActivities>.Instance);
    }

    // ── CreateWorktreeAsync ──────────────────────────────────────────────

    [Fact]
    public async Task CreateWorktreeAsync_CreatesBranchAndWorktree_WhenBranchMissing()
    {
        // Arrange — rev-parse returns non-zero (branch missing), branch + worktree add succeed.
        var docker = new Mock<IContainerManager>(MockBehavior.Strict);

        // rev-parse --verify --quiet  → non-zero (branch missing)
        docker.Setup(d => d.ExecAsync(
                   "ctr-1",
                   It.Is<string>(s => s.Contains("rev-parse --verify --quiet agent-a")),
                   "/repo", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new ExecResult(1, "", ""));

        // branch creation → success
        docker.Setup(d => d.ExecAsync(
                   "ctr-1",
                   It.Is<string>(s => s.Contains("branch agent-a main")),
                   "/repo", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new ExecResult(0, "", ""));

        // worktree add → success
        docker.Setup(d => d.ExecAsync(
                   "ctr-1",
                   It.Is<string>(s => s.Contains("worktree add /workspaces/worktrees/agent-a agent-a")),
                   "/repo", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new ExecResult(0, "Preparing worktree", ""));

        var sut = BuildSut(docker: docker);
        var input = new CreateWorktreeInput(
            ContainerId: "ctr-1",
            BranchName: "agent-a",
            RepoDirectory: "/repo",
            BaseBranch: "main");

        // Act
        var env = new ActivityEnvironment();
        var output = await env.RunAsync(() => sut.CreateWorktreeAsync(input));

        // Assert
        output.WorktreePath.Should().Be("/workspaces/worktrees/agent-a");
        output.CreatedFromScratch.Should().BeTrue();

        docker.VerifyAll();
    }

    // ── MergeWorktreeAsync ───────────────────────────────────────────────

    [Fact]
    public async Task MergeWorktreeAsync_SucceedsAndReturnsSha_WhenNoConflict()
    {
        // Arrange — checkout, merge, rev-parse all succeed. No push.
        var docker = new Mock<IContainerManager>(MockBehavior.Strict);

        docker.Setup(d => d.ExecAsync(
                   "ctr-1",
                   It.Is<string>(s => s.Contains("checkout main")),
                   "/repo", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new ExecResult(0, "Switched to 'main'", ""));

        docker.Setup(d => d.ExecAsync(
                   "ctr-1",
                   It.Is<string>(s => s.Contains("merge --no-ff agent-a")),
                   "/repo", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new ExecResult(0, "Merge made by 'no-ff'", ""));

        docker.Setup(d => d.ExecAsync(
                   "ctr-1",
                   It.Is<string>(s => s.Contains("rev-parse HEAD")),
                   "/repo", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new ExecResult(0, "abc1234\n", ""));

        var sut = BuildSut(docker: docker);
        var input = new MergeWorktreeInput(
            ContainerId: "ctr-1",
            BranchName: "agent-a",
            RepoDirectory: "/repo",
            TargetBranch: "main",
            PushAfterMerge: false);

        // Act
        var env = new ActivityEnvironment();
        var output = await env.RunAsync(() => sut.MergeWorktreeAsync(input));

        // Assert
        output.Merged.Should().BeTrue();
        output.ConflictReport.Should().BeNull();
        output.MergeCommitSha.Should().Be("abc1234");
    }

    [Fact]
    public async Task MergeWorktreeAsync_AbortsAndReturnsConflict_OnMergeFailure()
    {
        var docker = new Mock<IContainerManager>(MockBehavior.Strict);

        docker.Setup(d => d.ExecAsync(
                   It.IsAny<string>(),
                   It.Is<string>(s => s.Contains("checkout main")),
                   It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new ExecResult(0, "", ""));

        docker.Setup(d => d.ExecAsync(
                   It.IsAny<string>(),
                   It.Is<string>(s => s.Contains("merge --no-ff agent-b")),
                   It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new ExecResult(1, "CONFLICT (content): Merge conflict in foo.cs", ""));

        docker.Setup(d => d.ExecAsync(
                   It.IsAny<string>(),
                   It.Is<string>(s => s.Contains("merge --abort")),
                   It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new ExecResult(0, "", ""));

        var sut = BuildSut(docker: docker);
        var input = new MergeWorktreeInput(
            ContainerId: "ctr-2",
            BranchName: "agent-b",
            RepoDirectory: "/repo",
            TargetBranch: "main");

        var env = new ActivityEnvironment();
        var output = await env.RunAsync(() => sut.MergeWorktreeAsync(input));

        output.Merged.Should().BeFalse();
        output.ConflictReport.Should().Contain("CONFLICT");
        output.MergeCommitSha.Should().BeNull();
    }

    // ── CleanupWorktreeAsync ─────────────────────────────────────────────

    [Fact]
    public async Task CleanupWorktreeAsync_RemovesWorktreeAndOptionallyBranch()
    {
        var docker = new Mock<IContainerManager>(MockBehavior.Strict);

        docker.Setup(d => d.ExecAsync(
                   "ctr-3",
                   It.Is<string>(s => s.Contains("worktree remove --force /workspaces/worktrees/agent-c")),
                   "/repo", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new ExecResult(0, "", ""));

        docker.Setup(d => d.ExecAsync(
                   "ctr-3",
                   It.Is<string>(s => s.Contains("branch -D agent-c")),
                   "/repo", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new ExecResult(0, "Deleted branch agent-c", ""));

        var sut = BuildSut(docker: docker);
        var input = new CleanupWorktreeInput(
            ContainerId: "ctr-3",
            BranchName: "agent-c",
            RepoDirectory: "/repo",
            DeleteBranch: true);

        var env = new ActivityEnvironment();
        await env.RunAsync(() => sut.CleanupWorktreeAsync(input));

        docker.VerifyAll();
    }

    [Fact]
    public async Task CleanupWorktreeAsync_Throws_WhenContainerIdMissing()
    {
        var sut = BuildSut();
        var input = new CleanupWorktreeInput(
            ContainerId: "",
            BranchName: "agent-c",
            RepoDirectory: "/repo");

        var env = new ActivityEnvironment();
        Func<Task> act = async () => await env.RunAsync(() => sut.CleanupWorktreeAsync(input));

        await act.Should()
                 .ThrowAsync<ApplicationFailureException>()
                 .Where(e => e.ErrorType == "ConfigError");
    }
}
