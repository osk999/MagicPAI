using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Attributes;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;
using MagicPAI.Core.Services;

namespace MagicPAI.Activities.Git;

[Activity("MagicPAI", "Git", "Merge a worktree branch back into the target branch")]
[FlowNode("Done", "Failed")]
public class MergeWorktreeActivity : Activity
{
    [Input(DisplayName = "Container ID")]
    public Input<string> ContainerId { get; set; } = default!;

    [Input(DisplayName = "Branch Name")]
    public Input<string> BranchName { get; set; } = default!;

    [Input(DisplayName = "Target Branch")]
    public Input<string> TargetBranch { get; set; } = new("main");

    [Input(DisplayName = "Repo Directory")]
    public Input<string> RepoDirectory { get; set; } = new("/workspace");

    [Output(DisplayName = "Success")]
    public Output<bool> Success { get; set; } = default!;

    [Output(DisplayName = "Conflict Files")]
    public Output<string[]> ConflictFiles { get; set; } = default!;

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var containerMgr = context.GetRequiredService<IContainerManager>();
        var containerId = ContainerId.Get(context);
        var branchName = CreateWorktreeActivity.SanitizeBranchName(BranchName.Get(context) ?? "");
        var targetBranch = CreateWorktreeActivity.SanitizeBranchName(TargetBranch.Get(context) ?? "main");
        var repoDir = RepoDirectory.Get(context) ?? "/workspace";

        try
        {
            var mergeResult = await containerMgr.ExecAsync(
                containerId,
                $"git checkout {targetBranch} && git merge {branchName} --no-edit",
                repoDir,
                context.CancellationToken);

            if (mergeResult.ExitCode == 0)
            {
                Success.Set(context, true);
                ConflictFiles.Set(context, []);

                context.AddExecutionLogEntry("MergeComplete",
                    $"Merged {branchName} into {targetBranch}");

                await context.CompleteActivityWithOutcomesAsync("Done");
            }
            else
            {
                var conflictResult = await containerMgr.ExecAsync(
                    containerId,
                    "git diff --name-only --diff-filter=U",
                    repoDir,
                    context.CancellationToken);

                var conflicts = conflictResult.Output
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                Success.Set(context, false);
                ConflictFiles.Set(context, conflicts);

                // Abort the merge to leave a clean state
                await containerMgr.ExecAsync(
                    containerId, "git merge --abort", repoDir, context.CancellationToken);

                context.AddExecutionLogEntry("MergeFailed",
                    $"Conflicts in {conflicts.Length} file(s)");

                await context.CompleteActivityWithOutcomesAsync("Failed");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            context.AddExecutionLogEntry("MergeError", ex.Message);
            Success.Set(context, false);
            ConflictFiles.Set(context, []);
            await context.CompleteActivityWithOutcomesAsync("Failed");
        }
    }
}
