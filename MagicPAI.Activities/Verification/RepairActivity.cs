using System.Text;
using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Attributes;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;
using Elsa.Workflows.UIHints;
using MagicPAI.Activities;
using MagicPAI.Core.Config;

namespace MagicPAI.Activities.Verification;

[Activity("MagicPAI", "Verification",
    "Generate an AI repair prompt from failed verification gates")]
[FlowNode("Done", "Exceeded")]
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
        var config = context.GetRequiredService<MagicPaiConfig>();
        var failedGates = FailedGates.GetOrDefault(context, () => null)
            ?? TryGetVariable<string[]>(context, "FailedGates")
            ?? [];
        var originalPrompt = OriginalPrompt.GetOrDefault(context, () => "")
            ?? "";
        if (string.IsNullOrWhiteSpace(originalPrompt))
            originalPrompt = context.GetOptionalWorkflowInput<string>("Prompt") ?? "";
        var gateResultsJson = GateResultsJson.GetOrDefault(context, () => null)
            ?? TryGetVariable<string>(context, "GateResultsJson")
            ?? "[]";
        var attemptCount = TryGetVariable<int>(context, "RepairAttempts");
        var maxAttempts = Math.Max(0, config.MaxRepairAttempts);

        if (attemptCount >= maxAttempts)
        {
            context.AddExecutionLogEntry("RepairAttemptsExceeded",
                $"Repair attempts exhausted after {attemptCount}/{maxAttempts} tries");
            await context.CompleteActivityWithOutcomesAsync("Exceeded");
            return;
        }

        var nextAttempt = attemptCount + 1;
        context.SetVariable("RepairAttempts", nextAttempt);

        var prompt = BuildRepairPrompt(
            originalPrompt, failedGates, gateResultsJson, nextAttempt, maxAttempts);
        RepairPrompt.Set(context, prompt);
        context.SetVariable("RepairPrompt", prompt);

        context.AddExecutionLogEntry("RepairPromptGenerated",
            $"Repair prompt for {failedGates.Length} failed gate(s), attempt {nextAttempt}/{maxAttempts}");

        await context.CompleteActivityWithOutcomesAsync("Done");
    }

    private static T? TryGetVariable<T>(ActivityExecutionContext context, string name)
    {
        try
        {
            return context.GetVariable<T>(name);
        }
        catch
        {
            return default;
        }
    }

    private static string BuildRepairPrompt(
        string originalPrompt, string[] failedGates, string gateResultsJson,
        int attemptNumber, int maxAttempts)
    {
        var sb = new StringBuilder();
        sb.AppendLine("The previous attempt had verification failures. Fix them.");
        sb.AppendLine();
        sb.AppendLine($"Repair attempt: {attemptNumber}/{maxAttempts}");
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
