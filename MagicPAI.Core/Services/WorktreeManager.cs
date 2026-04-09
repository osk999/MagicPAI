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
        var safeBranch = SanitizeName(branchName);
        var safePath = SanitizePath(worktreePath);
        await _env.RunCommandAsync(
            $"git worktree add -b '{safeBranch}' '{safePath}'", repoDir, ct);
        return worktreePath;
    }

    /// <summary>Merge the worktree branch back into the target branch.</summary>
    public async Task<string> MergeWorktreeAsync(string repoDir, string branchName,
        string targetBranch, CancellationToken ct)
    {
        var safeBranch = SanitizeName(branchName);
        var safeTarget = SanitizeName(targetBranch);
        var output = await _env.RunCommandAsync(
            $"git checkout '{safeTarget}' && git merge '{safeBranch}' --no-edit", repoDir, ct);
        return output;
    }

    /// <summary>Clean up a worktree and its branch.</summary>
    public async Task CleanupWorktreeAsync(string repoDir, string worktreePath,
        string branchName, CancellationToken ct)
    {
        var safePath = SanitizePath(worktreePath);
        var safeBranch = SanitizeName(branchName);
        await _env.RunCommandAsync(
            $"git worktree remove '{safePath}' --force", repoDir, ct);
        await _env.RunCommandAsync(
            $"git branch -D '{safeBranch}'", repoDir, ct);
    }

    /// <summary>Sanitize branch name to prevent shell injection.</summary>
    private static string SanitizeName(string name) =>
        new(name.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or '/').ToArray());

    /// <summary>Sanitize file path — allow alphanumeric, path separators, dots, hyphens, underscores.</summary>
    private static string SanitizePath(string path) =>
        new(path.Where(c => char.IsLetterOrDigit(c) || c is '/' or '\\' or '-' or '_' or '.').ToArray());
}
