using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Temporalio.Activities;
using Temporalio.Exceptions;
using MagicPAI.Activities.Contracts;
using MagicPAI.Core.Services;

namespace MagicPAI.Activities.Git;

/// <summary>
/// Temporal activity group for git worktree lifecycle inside a worker container.
/// All operations run via <see cref="IContainerManager.ExecAsync(string, string, string, CancellationToken)"/>
/// against a container that already has git installed and a cloned repository at
/// <see cref="CreateWorktreeInput.RepoDirectory"/>. See temporal.md §I.2 and §7.4.
/// </summary>
/// <remarks>
/// <para>
/// Parallel agents in the complex-orchestration path share a single container's
/// filesystem through worktrees: each agent gets an isolated checkout under
/// <c>/workspaces/worktrees/{branchName}</c> and merges its branch back to the
/// target branch at the end.
/// </para>
/// <para>
/// All git commands are invoked with <c>git -C {RepoDirectory}</c> so the activity
/// does not rely on the container's shell <c>cd</c>; the <see cref="IContainerManager"/>
/// working-directory argument is set to <see cref="CreateWorktreeInput.RepoDirectory"/>
/// for parity with how the Elsa implementation ran them.
/// </para>
/// </remarks>
public class GitActivities
{
    private readonly IContainerManager _docker;
    private readonly ILogger<GitActivities> _log;

    public GitActivities(IContainerManager docker, ILogger<GitActivities>? log = null)
    {
        _docker = docker;
        _log = log ?? NullLogger<GitActivities>.Instance;
    }

    /// <summary>
    /// Strip characters that could enable shell injection from a branch name.
    /// Keeps only letters, digits, hyphens, underscores, dots, and forward slashes.
    /// Kept as a static helper so tests can exercise it without a container.
    /// </summary>
    public static string SanitizeBranchName(string name) =>
        new(name.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or '/').ToArray());

    /// <summary>
    /// Create a git worktree for <paramref name="input.BranchName"/>. If the branch
    /// does not yet exist, it is created from <paramref name="input.BaseBranch"/>.
    /// The worktree is placed at <c>/workspaces/worktrees/{BranchName}</c>.
    /// </summary>
    [Activity]
    public async Task<CreateWorktreeOutput> CreateWorktreeAsync(CreateWorktreeInput input)
    {
        if (string.IsNullOrWhiteSpace(input.ContainerId))
            throw new ApplicationFailureException(
                "CreateWorktree requires a ContainerId; git activities always run inside a worker container.",
                errorType: "ConfigError", nonRetryable: true);
        if (string.IsNullOrWhiteSpace(input.BranchName))
            throw new ApplicationFailureException(
                "CreateWorktree requires a non-empty BranchName.",
                errorType: "ConfigError", nonRetryable: true);

        var ct = ActivityExecutionContext.Current.CancellationToken;
        var safeName = input.BranchName.Replace('/', '_').Replace('\\', '_');
        var worktreeRoot = "/tmp/magicpai-worktrees";
        var worktreePath = $"{worktreeRoot}/{safeName}";
        await _docker.ExecAsync(input.ContainerId, $"mkdir -p {worktreeRoot}", input.RepoDirectory, ct);

        var isRepo = await _docker.ExecAsync(
            input.ContainerId,
            $"git -C {input.RepoDirectory} rev-parse --is-inside-work-tree",
            input.RepoDirectory, ct);
        if (isRepo.ExitCode != 0)
        {
            _log.LogInformation("Initializing git repo at {Repo} (no .git found)", input.RepoDirectory);
            var initBranch = string.IsNullOrWhiteSpace(input.BaseBranch) ? "main" : input.BaseBranch;
            var init = await _docker.ExecAsync(
                input.ContainerId,
                $"git -C {input.RepoDirectory} init -q -b {initBranch}",
                input.RepoDirectory, ct);
            if (init.ExitCode != 0)
                throw new ApplicationFailureException(
                    $"Failed to git init {input.RepoDirectory}: {init.Error}",
                    errorType: "GitError", nonRetryable: false);
            await _docker.ExecAsync(input.ContainerId,
                $"git -C {input.RepoDirectory} -c user.email=magicpai@local -c user.name=MagicPAI commit --allow-empty -q -m \"magicpai: initial commit\"",
                input.RepoDirectory, ct);
        }

        var checkBranch = await _docker.ExecAsync(
            input.ContainerId,
            $"git -C {input.RepoDirectory} rev-parse --verify --quiet {input.BranchName}",
            input.RepoDirectory, ct);
        var branchExists = checkBranch.ExitCode == 0;

        if (!branchExists)
        {
            _log.LogInformation("Creating branch {Branch} from {Base}", input.BranchName, input.BaseBranch);
            var createBranch = await _docker.ExecAsync(
                input.ContainerId,
                $"git -C {input.RepoDirectory} branch {input.BranchName} {input.BaseBranch}",
                input.RepoDirectory, ct);

            if (createBranch.ExitCode != 0)
                throw new ApplicationFailureException(
                    $"Failed to create branch {input.BranchName}: {createBranch.Error}",
                    errorType: "GitError", nonRetryable: false);
        }

        _log.LogInformation("Adding worktree at {Path} for branch {Branch}", worktreePath, input.BranchName);
        var addResult = await _docker.ExecAsync(
            input.ContainerId,
            $"git -C {input.RepoDirectory} worktree add {worktreePath} {input.BranchName}",
            input.RepoDirectory, ct);

        if (addResult.ExitCode != 0)
            throw new ApplicationFailureException(
                $"Failed to add worktree at {worktreePath}: {addResult.Error}",
                errorType: "GitError", nonRetryable: false);

        return new CreateWorktreeOutput(
            WorktreePath: worktreePath,
            CreatedFromScratch: !branchExists);
    }

    /// <summary>
    /// Stage and commit any uncommitted changes inside the worktree for
    /// <paramref name="input.BranchName"/>. No-op if there are no changes.
    /// Without this, work the agent wrote in the worktree never reaches the
    /// branch tip and is silently lost when the worktree is removed.
    /// </summary>
    [Activity]
    public async Task<CommitWorktreeOutput> CommitWorktreeAsync(CommitWorktreeInput input)
    {
        if (string.IsNullOrWhiteSpace(input.ContainerId))
            throw new ApplicationFailureException(
                "CommitWorktree requires a ContainerId.",
                errorType: "ConfigError", nonRetryable: true);
        if (string.IsNullOrWhiteSpace(input.WorktreePath))
            throw new ApplicationFailureException(
                "CommitWorktree requires a non-empty WorktreePath.",
                errorType: "ConfigError", nonRetryable: true);

        var ct = ActivityExecutionContext.Current.CancellationToken;
        var worktreePath = input.WorktreePath;

        await _docker.ExecAsync(input.ContainerId,
            $"git -C {worktreePath} add -A",
            worktreePath, ct);

        var diff = await _docker.ExecAsync(input.ContainerId,
            $"git -C {worktreePath} diff --cached --quiet",
            worktreePath, ct);
        if (diff.ExitCode == 0)
        {
            _log.LogInformation("CommitWorktree {Path}: no changes to commit", worktreePath);
            return new CommitWorktreeOutput(DidCommit: false, CommitSha: null);
        }

        var safeMessage = input.Message.Replace("'", "'\\''");
        var commit = await _docker.ExecAsync(input.ContainerId,
            $"git -C {worktreePath} -c user.email=magicpai@local -c user.name=MagicPAI commit -m '{safeMessage}'",
            worktreePath, ct);
        if (commit.ExitCode != 0)
            throw new ApplicationFailureException(
                $"Failed to commit in worktree {worktreePath}: {commit.Error}",
                errorType: "GitError", nonRetryable: false);

        var sha = await _docker.ExecAsync(input.ContainerId,
            $"git -C {worktreePath} rev-parse HEAD",
            worktreePath, ct);

        _log.LogInformation("CommitWorktree {Path} committed as {Sha}",
            worktreePath, sha.Output?.Trim());
        return new CommitWorktreeOutput(DidCommit: true, CommitSha: sha.Output?.Trim());
    }

    /// <summary>
    /// Check out <paramref name="input.TargetBranch"/> and merge
    /// <paramref name="input.BranchName"/> into it with a no-fast-forward merge
    /// commit. On conflict, <c>merge --abort</c> is issued and
    /// <see cref="MergeWorktreeOutput.Merged"/> is <c>false</c>.
    /// </summary>
    [Activity]
    public async Task<MergeWorktreeOutput> MergeWorktreeAsync(MergeWorktreeInput input)
    {
        if (string.IsNullOrWhiteSpace(input.ContainerId))
            throw new ApplicationFailureException(
                "MergeWorktree requires a ContainerId.",
                errorType: "ConfigError", nonRetryable: true);

        var ct = ActivityExecutionContext.Current.CancellationToken;

        var checkout = await _docker.ExecAsync(
            input.ContainerId,
            $"git -C {input.RepoDirectory} checkout {input.TargetBranch}",
            input.RepoDirectory, ct);

        if (checkout.ExitCode != 0)
            throw new ApplicationFailureException(
                $"Failed to checkout {input.TargetBranch}: {checkout.Error}",
                errorType: "GitError", nonRetryable: false);

        var mergeResult = await _docker.ExecAsync(
            input.ContainerId,
            $"git -C {input.RepoDirectory} merge --no-ff {input.BranchName} -m 'merge {input.BranchName}'",
            input.RepoDirectory, ct);

        if (mergeResult.ExitCode != 0)
        {
            _log.LogWarning("Merge of {Branch} into {Target} conflicted; aborting",
                input.BranchName, input.TargetBranch);
            await _docker.ExecAsync(
                input.ContainerId,
                $"git -C {input.RepoDirectory} merge --abort",
                input.RepoDirectory, ct);
            return new MergeWorktreeOutput(
                Merged: false,
                ConflictReport: mergeResult.Output,
                MergeCommitSha: null);
        }

        var sha = await _docker.ExecAsync(
            input.ContainerId,
            $"git -C {input.RepoDirectory} rev-parse HEAD",
            input.RepoDirectory, ct);

        if (input.PushAfterMerge)
        {
            var push = await _docker.ExecAsync(
                input.ContainerId,
                $"git -C {input.RepoDirectory} push origin {input.TargetBranch}",
                input.RepoDirectory, ct);

            if (push.ExitCode != 0)
                _log.LogWarning("Push of {Target} failed after successful merge: {Err}",
                    input.TargetBranch, push.Error);
        }

        return new MergeWorktreeOutput(
            Merged: true,
            ConflictReport: null,
            MergeCommitSha: sha.Output?.Trim());
    }

    /// <summary>
    /// Remove the worktree for <paramref name="input.BranchName"/> and optionally
    /// delete the branch. <c>worktree remove --force</c> is used so outstanding
    /// uncommitted changes in the worktree don't block cleanup.
    /// </summary>
    [Activity]
    public async Task CleanupWorktreeAsync(CleanupWorktreeInput input)
    {
        if (string.IsNullOrWhiteSpace(input.ContainerId))
            throw new ApplicationFailureException(
                "CleanupWorktree requires a ContainerId.",
                errorType: "ConfigError", nonRetryable: true);

        var ct = ActivityExecutionContext.Current.CancellationToken;
        var safeName = input.BranchName.Replace('/', '_').Replace('\\', '_');
        var worktreePath = $"/tmp/magicpai-worktrees/{safeName}";

        // Best-effort cleanup: if the worktree is already gone, log and proceed.
        var removeResult = await _docker.ExecAsync(
            input.ContainerId,
            $"git -C {input.RepoDirectory} worktree remove --force {worktreePath}",
            input.RepoDirectory, ct);

        if (removeResult.ExitCode != 0)
            _log.LogDebug("Worktree remove returned {ExitCode} (already gone?): {Err}",
                removeResult.ExitCode, removeResult.Error);

        if (input.DeleteBranch)
        {
            var delBranch = await _docker.ExecAsync(
                input.ContainerId,
                $"git -C {input.RepoDirectory} branch -D {input.BranchName}",
                input.RepoDirectory, ct);

            if (delBranch.ExitCode != 0)
                _log.LogDebug("Branch delete returned {ExitCode}: {Err}",
                    delBranch.ExitCode, delBranch.Error);
        }
    }
}
