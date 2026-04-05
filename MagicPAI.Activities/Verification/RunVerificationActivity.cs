using System.Text.Json;
using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Attributes;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;
using Elsa.Workflows.UIHints;
using MagicPAI.Core.Services;

namespace MagicPAI.Activities.Verification;

[Activity("MagicPAI", "Verification",
    "Run verification gates (compile, test, security, etc.) in a container")]
[FlowNode("Passed", "Failed", "Inconclusive")]
public class RunVerificationActivity : Activity
{
    [Input(DisplayName = "Container ID")]
    public Input<string> ContainerId { get; set; } = default!;

    [Input(DisplayName = "Working Directory")]
    public Input<string> WorkingDirectory { get; set; } = new("/workspace");

    [Input(DisplayName = "Gates to Run",
        UIHint = InputUIHints.CheckList,
        Options = new[] { "compile", "test", "coverage", "security",
                          "lint", "hallucination", "quality" },
        Description = "Which verification gates to execute")]
    public Input<string[]> Gates { get; set; } =
        new(new[] { "compile", "test", "hallucination" });

    [Input(DisplayName = "Worker Output (for hallucination check)",
        UIHint = InputUIHints.MultiLine,
        Category = "Context")]
    public Input<string?> WorkerOutput { get; set; } = default!;

    [Output(DisplayName = "All Passed")]
    public Output<bool> AllPassed { get; set; } = default!;

    [Output(DisplayName = "Failed Gates")]
    public Output<string[]> FailedGates { get; set; } = default!;

    [Output(DisplayName = "Gate Results JSON")]
    public Output<string> GateResultsJson { get; set; } = default!;

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var containerMgr = context.GetRequiredService<IContainerManager>();
        var pipeline = context.GetRequiredService<VerificationPipeline>();

        try
        {
            var containerId = ContainerId.Get(context);
            var workDir = WorkingDirectory.Get(context) ?? "/workspace";
            var gates = Gates.Get(context) ?? ["compile", "test", "hallucination"];
            var workerOutput = WorkerOutput.GetOrDefault(context, () => null);

            var result = await pipeline.RunAsync(
                containerMgr, containerId, workDir, gates,
                workerOutput, context.CancellationToken);

            AllPassed.Set(context, result.AllPassed);
            FailedGates.Set(context,
                result.Gates.Where(g => !g.Passed).Select(g => g.Name).ToArray());
            GateResultsJson.Set(context,
                JsonSerializer.Serialize(result.Gates));

            context.AddExecutionLogEntry("VerificationComplete",
                $"AllPassed={result.AllPassed}, Gates={result.Gates.Count}");

            var outcome = result.IsInconclusive ? "Inconclusive"
                : result.AllPassed ? "Passed" : "Failed";
            await context.CompleteActivityWithOutcomesAsync(outcome);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            context.AddExecutionLogEntry("VerificationFailed", ex.Message);
            AllPassed.Set(context, false);
            FailedGates.Set(context, ["pipeline-error"]);
            GateResultsJson.Set(context, "[]");
            await context.CompleteActivityWithOutcomesAsync("Failed");
        }
    }
}
