using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Attributes;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;
using Elsa.Workflows.UIHints;
using MagicPAI.Core.Services;

namespace MagicPAI.Activities.Docker;

[Activity("MagicPAI", "Docker", "Run a shell command inside a Docker container")]
[FlowNode("Done", "Failed")]
public class ExecInContainerActivity : Activity
{
    [Input(DisplayName = "Container ID")]
    public Input<string> ContainerId { get; set; } = default!;

    [Input(DisplayName = "Command", UIHint = InputUIHints.MultiLine)]
    public Input<string> Command { get; set; } = default!;

    [Input(DisplayName = "Working Directory")]
    public Input<string> WorkingDirectory { get; set; } = new("/workspace");

    [Output(DisplayName = "Output")]
    public Output<string> Output { get; set; } = default!;

    [Output(DisplayName = "Exit Code")]
    public Output<int> ExitCode { get; set; } = default!;

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var docker = context.GetRequiredService<IContainerManager>();

        try
        {
            var result = await docker.ExecAsync(
                ContainerId.Get(context),
                Command.Get(context) ?? "",
                WorkingDirectory.Get(context) ?? "/workspace",
                context.CancellationToken);

            Output.Set(context, result.Output);
            ExitCode.Set(context, result.ExitCode);

            context.AddExecutionLogEntry("ExecComplete",
                $"Exit code: {result.ExitCode}");

            await context.CompleteActivityWithOutcomesAsync(
                result.ExitCode == 0 ? "Done" : "Failed");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            context.AddExecutionLogEntry("ExecFailed", ex.Message);
            Output.Set(context, ex.Message);
            ExitCode.Set(context, -1);
            await context.CompleteActivityWithOutcomesAsync("Failed");
        }
    }
}
