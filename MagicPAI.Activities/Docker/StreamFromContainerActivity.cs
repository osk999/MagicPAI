using System.Text;
using System.Text.Json;
using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Attributes;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;
using Elsa.Workflows.UIHints;
using MagicPAI.Core.Services;

namespace MagicPAI.Activities.Docker;

[Activity("MagicPAI", "Docker", "Stream real-time output from a command in a Docker container")]
[FlowNode("Done", "Failed")]
public class StreamFromContainerActivity : Activity
{
    [Input(DisplayName = "Container ID")]
    public Input<string> ContainerId { get; set; } = default!;

    [Input(DisplayName = "Command", UIHint = InputUIHints.MultiLine)]
    public Input<string> Command { get; set; } = default!;

    [Input(DisplayName = "Timeout (minutes)", Category = "Limits")]
    public Input<int> TimeoutMinutes { get; set; } = new(30);

    [Output(DisplayName = "Full Output")]
    public Output<string> FullOutput { get; set; } = default!;

    [Output(DisplayName = "Exit Code")]
    public Output<int> ExitCode { get; set; } = default!;

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var docker = context.GetRequiredService<IContainerManager>();
        var outputBuilder = new StringBuilder();

        try
        {
            var result = await docker.ExecStreamingAsync(
                ContainerId.Get(context),
                Command.Get(context) ?? "",
                onOutput: chunk =>
                {
                    outputBuilder.Append(chunk);
                    context.AddExecutionLogEntry("StreamChunk",
                        JsonSerializer.Serialize(new
                        {
                            activityId = context.Activity.Id,
                            text = chunk
                        }));
                },
                timeout: TimeSpan.FromMinutes(TimeoutMinutes.Get(context)),
                ct: context.CancellationToken);

            FullOutput.Set(context, outputBuilder.ToString());
            ExitCode.Set(context, result.ExitCode);

            await context.CompleteActivityWithOutcomesAsync(
                result.ExitCode == 0 ? "Done" : "Failed");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            context.AddExecutionLogEntry("StreamFailed", ex.Message);
            FullOutput.Set(context, outputBuilder.ToString());
            ExitCode.Set(context, -1);
            await context.CompleteActivityWithOutcomesAsync("Failed");
        }
    }
}
