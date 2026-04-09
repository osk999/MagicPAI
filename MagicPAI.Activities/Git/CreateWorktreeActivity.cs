using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Attributes;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;
using MagicPAI.Core.Services;

namespace MagicPAI.Activities.Git;

[Activity("MagicPAI", "Git", "Create a git worktree for isolated branch work in a container")]
[FlowNode("Done", "Failed")]
public class CreateWorktreeActivity : Activity
{
    [Input(DisplayName = "Container ID")]
    public Input<string> ContainerId { get; set; } = default!;

    [Input(DisplayName = "Branch Name")]
    public Input<string> BranchName { get; set; } = default!;

    [Input(DisplayName = "Repo Directory")]
    public Input<string> RepoDirectory { get; set; } = new("/workspace");

    [Output(DisplayName = "Worktree Path")]
    public Output<string> WorktreePath { get; set; } = default!;

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var containerMgr = context.GetRequiredService<IContainerManager>();
        var containerId = ContainerId.Get(context);
        var branchName = SanitizeBranchName(BranchName.Get(context) ?? "");
        var repoDir = RepoDirectory.Get(context) ?? "/workspace";
        var worktreePath = $"/workspace/worktrees/{branchName}";

        try
        {
            var result = await containerMgr.ExecAsync(
                containerId,
                $"git worktree add -b '{branchName}' '{worktreePath}'",
                repoDir,
                context.CancellationToken);

            if (result.ExitCode != 0)
            {
                context.AddExecutionLogEntry("WorktreeCreateFailed", result.Error);
                await context.CompleteActivityWithOutcomesAsync("Failed");
                return;
            }

            WorktreePath.Set(context, worktreePath);

            context.AddExecutionLogEntry("WorktreeCreated",
                $"Branch {branchName} at {worktreePath}");

            await context.CompleteActivityWithOutcomesAsync("Done");
        }
        catch (Exception ex)
        {
            context.AddExecutionLogEntry("WorktreeCreateFailed", ex.Message);
            await context.CompleteActivityWithOutcomesAsync("Failed");
        }
    }

    /// <summary>Sanitize branch name to prevent shell injection.</summary>
    public static string SanitizeBranchName(string name)
    {
        // Allow only alphanumeric, hyphens, underscores, dots, and slashes
        return new string(name.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or '/').ToArray());
    }
}
