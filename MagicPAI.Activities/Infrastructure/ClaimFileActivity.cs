using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Attributes;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;
using MagicPAI.Core.Services;

namespace MagicPAI.Activities.Infrastructure;

[Activity("MagicPAI", "Infrastructure",
    "Claim atomic file ownership via SharedBlackboard for parallel worker coordination")]
[FlowNode("Claimed", "AlreadyClaimed")]
public class ClaimFileActivity : Activity
{
    [Input(DisplayName = "File Path")]
    public Input<string> FilePath { get; set; } = default!;

    [Input(DisplayName = "Task ID")]
    public Input<string> TaskId { get; set; } = default!;

    [Output(DisplayName = "Claimed")]
    public Output<bool> Claimed { get; set; } = default!;

    [Output(DisplayName = "Current Owner")]
    public Output<string?> CurrentOwner { get; set; } = default!;

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var blackboard = context.GetRequiredService<SharedBlackboard>();
        var filePath = FilePath.Get(context);
        var taskId = TaskId.Get(context);

        var claimed = blackboard.ClaimFile(filePath, taskId);
        Claimed.Set(context, claimed);

        if (claimed)
        {
            CurrentOwner.Set(context, taskId);
            context.AddExecutionLogEntry("FileClaimed",
                $"Task {taskId} claimed {filePath}");
            await context.CompleteActivityWithOutcomesAsync("Claimed");
        }
        else
        {
            var owner = blackboard.GetFileOwner(filePath);
            CurrentOwner.Set(context, owner);
            context.AddExecutionLogEntry("FileAlreadyClaimed",
                $"{filePath} already owned by {owner}");
            await context.CompleteActivityWithOutcomesAsync("AlreadyClaimed");
        }
    }
}
