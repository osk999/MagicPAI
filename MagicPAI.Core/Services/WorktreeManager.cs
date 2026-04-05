namespace MagicPAI.Core.Services;

public class WorktreeManager
{
    private readonly IExecutionEnvironment _env;

    public WorktreeManager(IExecutionEnvironment env)
    {
        _env = env;
    }

    /// <summary>Create a git worktree for isolated branch work.</summary>
    public async Task<string> CreateWorktreeAsync(string repoDir, string branchName,
        string worktreePath, CancellationToken ct)
    {
        await _env.RunCommandAsync(
            $"git worktree add -b {branchName} {worktreePath}", repoDir, ct);
        return worktreePath;
    }

    /// <summary>Merge the worktree branch back into the target branch.</summary>
    public async Task<string> MergeWorktreeAsync(string repoDir, string branchName,
        string targetBranch, CancellationToken ct)
    {
        var output = await _env.RunCommandAsync(
            $"git checkout {targetBranch} && git merge {branchName} --no-edit", repoDir, ct);
        return output;
    }

    /// <summary>Clean up a worktree and its branch.</summary>
    public async Task CleanupWorktreeAsync(string repoDir, string worktreePath,
        string branchName, CancellationToken ct)
    {
        await _env.RunCommandAsync(
            $"git worktree remove {worktreePath} --force", repoDir, ct);
        await _env.RunCommandAsync(
            $"git branch -D {branchName}", repoDir, ct);
    }
}
