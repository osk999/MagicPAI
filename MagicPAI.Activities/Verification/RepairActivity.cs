using System.Text;
using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Attributes;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;
using Elsa.Workflows.UIHints;

namespace MagicPAI.Activities.Verification;

[Activity("MagicPAI", "Verification",
    "Generate an AI repair prompt from failed verification gates")]
[FlowNode("Done")]
public class RepairActivity : Activity
{
    [Input(DisplayName = "Container ID")]
    public Input<string> ContainerId { get; set; } = default!;

    [Input(DisplayName = "Failed Gates")]
    public Input<string[]> FailedGates { get; set; } = default!;

    [Input(DisplayName = "Original Prompt", UIHint = InputUIHints.MultiLine)]
    public Input<string> OriginalPrompt { get; set; } = default!;

    [Input(DisplayName = "Gate Results JSON", UIHint = InputUIHints.MultiLine)]
    public Input<string> GateResultsJson { get; set; } = default!;

    [Output(DisplayName = "Repair Prompt")]
    public Output<string> RepairPrompt { get; set; } = default!;

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var failedGates = FailedGates.Get(context) ?? [];
        var originalPrompt = OriginalPrompt.Get(context) ?? "";
        var gateResultsJson = GateResultsJson.Get(context) ?? "[]";

        var prompt = BuildRepairPrompt(originalPrompt, failedGates, gateResultsJson);
        RepairPrompt.Set(context, prompt);

        context.AddExecutionLogEntry("RepairPromptGenerated",
            $"Repair prompt for {failedGates.Length} failed gate(s)");

        await context.CompleteActivityWithOutcomesAsync("Done");
    }

    private static string BuildRepairPrompt(
        string originalPrompt, string[] failedGates, string gateResultsJson)
    {
        var sb = new StringBuilder();
        sb.AppendLine("The previous attempt had verification failures. Fix them.");
        sb.AppendLine();
        sb.AppendLine("## Original Task");
        sb.AppendLine(originalPrompt);
        sb.AppendLine();
        sb.AppendLine("## Failed Gates");
        foreach (var gate in failedGates)
        {
            sb.AppendLine($"- {gate}");
        }
        sb.AppendLine();
        sb.AppendLine("## Gate Results Details");
        sb.AppendLine(gateResultsJson);
        sb.AppendLine();
        sb.AppendLine("## Instructions");
        sb.AppendLine("1. Read the error output from each failed gate carefully.");
        sb.AppendLine("2. Fix ONLY the issues causing failures — do not refactor unrelated code.");
        sb.AppendLine("3. Ensure all previously passing gates still pass.");
        sb.AppendLine("4. If a test fails, fix the code (not the test) unless the test is wrong.");

        return sb.ToString();
    }
}
