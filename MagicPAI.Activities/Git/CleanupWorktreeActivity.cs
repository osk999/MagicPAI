using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Attributes;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;
using MagicPAI.Core.Services;

namespace MagicPAI.Activities.Git;

[Activity("MagicPAI", "Git", "Remove a git worktree and its branch")]
[FlowNode("Done", "Failed")]
public class CleanupWorktreeActivity : Activity
{
    [Input(DisplayName = "Container ID")]
    public Input<string> ContainerId { get; set; } = default!;

    [Input(DisplayName = "Worktree Path")]
    public Input<string> WorktreePath { get; set; } = default!;

    [Input(DisplayName = "Branch Name",
        Description = "Branch to delete after removing worktree (optional)")]
    public Input<string?> BranchName { get; set; } = default!;

    [Input(DisplayName = "Repo Directory")]
    public Input<string> RepoDirectory { get; set; } = new("/workspace");

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var containerMgr = context.GetRequiredService<IContainerManager>();
        var containerId = ContainerId.Get(context);
        var worktreePath = WorktreePath.Get(context) ?? "";
        var repoDir = RepoDirectory.Get(context) ?? "/workspace";
        var branchName = BranchName.GetOrDefault(context, () => null);
        if (!string.IsNullOrEmpty(branchName))
            branchName = CreateWorktreeActivity.SanitizeBranchName(branchName);

        try
        {
            // Remove the worktree
            await containerMgr.ExecAsync(
                containerId,
                $"git worktree remove {worktreePath} --force",
                repoDir,
                context.CancellationToken);

            // Optionally delete the branch
            if (!string.IsNullOrEmpty(branchName))
            {
                await containerMgr.ExecAsync(
                    containerId,
                    $"git branch -D {branchName}",
                    repoDir,
                    context.CancellationToken);
            }

            context.AddExecutionLogEntry("WorktreeCleanup",
                $"Removed worktree {worktreePath}");

            await context.CompleteActivityWithOutcomesAsync("Done");
        }
        catch (Exception ex)
        {
            context.AddExecutionLogEntry("WorktreeCleanupFailed", ex.Message);
            await context.CompleteActivityWithOutcomesAsync("Failed");
        }
    }
}
