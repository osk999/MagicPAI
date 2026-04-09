using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Attributes;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;
using Elsa.Workflows.UIHints;
using MagicPAI.Activities;

namespace MagicPAI.Activities.Infrastructure;

[Activity("MagicPAI", "Control Flow", "Pause workflow for human approval before proceeding")]
[FlowNode("Approved", "Rejected")]
public class HumanApprovalActivity : Activity
{
    [Input(DisplayName = "Message", UIHint = InputUIHints.MultiLine,
        Description = "Message to display to the approver")]
    public Input<string> Message { get; set; } = default!;

    [Input(DisplayName = "Options",
        UIHint = InputUIHints.CheckList,
        Options = new[] { "approve", "reject", "modify" })]
    public Input<string[]> Options { get; set; } =
        new(new[] { "approve", "reject" });

    [Output(DisplayName = "Decision")]
    public Output<string> Decision { get; set; } = default!;

    [Output(DisplayName = "Comment")]
    public Output<string?> Comment { get; set; } = default!;

    protected override ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        // Create a bookmark to suspend the workflow and wait for human input
        context.CreateBookmark(
            new ApprovalBookmarkPayload(Message.Get(context), Options.Get(context)),
            OnResumed,
            includeActivityInstanceId: true);

        return ValueTask.CompletedTask;
    }

    private async ValueTask OnResumed(ActivityExecutionContext context)
    {
        var decision = context.GetOptionalWorkflowInput<string>("Decision") ?? "reject";
        var comment = context.GetOptionalWorkflowInput<string>("Comment");

        Decision.Set(context, decision);
        Comment.Set(context, comment);

        context.AddExecutionLogEntry("HumanDecision",
            $"Decision: {decision}");

        var outcome = decision.Equals("approve", StringComparison.OrdinalIgnoreCase)
            ? "Approved"
            : "Rejected";
        await context.CompleteActivityWithOutcomesAsync(outcome);
    }
}

/// <summary>Bookmark payload for human approval, used to identify the bookmark type.</summary>
public record ApprovalBookmarkPayload(string Message, string[] Options);
