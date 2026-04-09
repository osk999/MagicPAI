using System.Text.Json;
using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Attributes;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;
using Elsa.Workflows.UIHints;
using MagicPAI.Activities;
using MagicPAI.Core.Config;
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
        var config = context.GetRequiredService<MagicPaiConfig>();

        try
        {
            var containerId = ContainerId.GetOrDefault(context, () => "");
            if (string.IsNullOrEmpty(containerId))
                containerId = TryGetVariable<string>(context, "ContainerId") ?? "";
            if (string.IsNullOrEmpty(containerId))
                containerId = context.GetOptionalWorkflowInput<string>("ContainerId") ?? "";
            var workDir = config.UseWorkerContainers
                ? WorkingDirectory.Get(context) ?? config.ContainerWorkDir ?? "/workspace"
                : context.GetOptionalWorkflowInput<string>("WorkspacePath")
                    ?? WorkingDirectory.Get(context)
                    ?? config.WorkspacePath
                    ?? ".";
            var gates = Gates.Get(context) ?? ["compile", "test", "hallucination"];
            var workerOutput = WorkerOutput.GetOrDefault(context, () => null);
            if (string.IsNullOrWhiteSpace(workerOutput))
                workerOutput = TryGetVariable<string>(context, "WorkerOutput");
            if (string.IsNullOrWhiteSpace(workerOutput))
                workerOutput = TryGetVariable<string>(context, "ComplexWorkerOutput");
            if (string.IsNullOrWhiteSpace(workerOutput))
                workerOutput = TryGetVariable<string>(context, "SimpleWorkerOutput");
            if (string.IsNullOrWhiteSpace(workerOutput))
                workerOutput = TryGetVariable<string>(context, "LastAgentResponse");
            if (string.IsNullOrWhiteSpace(workerOutput))
                workerOutput = context.GetOptionalWorkflowInput<string>("WorkerOutput");

            var result = await pipeline.RunAsync(
                containerMgr, containerId, workDir, gates,
                workerOutput, context.CancellationToken);

            var failedGates = result.Gates.Where(g => !g.Passed).Select(g => g.Name).ToArray();
            var gateResultsJson = JsonSerializer.Serialize(result.Gates);

            AllPassed.Set(context, result.AllPassed);
            FailedGates.Set(context, failedGates);
            GateResultsJson.Set(context, gateResultsJson);

            context.SetVariable("AllPassed", result.AllPassed);
            context.SetVariable("FailedGates", failedGates);
            context.SetVariable("GateResultsJson", gateResultsJson);

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
            context.SetVariable("AllPassed", false);
            context.SetVariable("FailedGates", new[] { "pipeline-error" });
            context.SetVariable("GateResultsJson", "[]");
            await context.CompleteActivityWithOutcomesAsync("Failed");
        }
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
}
