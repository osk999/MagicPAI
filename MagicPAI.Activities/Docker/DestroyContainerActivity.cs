using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Attributes;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;
using MagicPAI.Core.Services;

namespace MagicPAI.Activities.Docker;

[Activity("MagicPAI", "Docker", "Stop and remove a Docker container")]
[FlowNode("Done", "Failed")]
public class DestroyContainerActivity : Activity
{
    [Input(DisplayName = "Container ID")]
    public Input<string> ContainerId { get; set; } = default!;

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var docker = context.GetRequiredService<IContainerManager>();
        var containerId = ContainerId.Get(context) ?? "";

        try
        {
            if (string.IsNullOrEmpty(containerId))
            {
                context.AddExecutionLogEntry("ContainerDestroySkipped", "No container ID provided");
                await context.CompleteActivityWithOutcomesAsync("Done");
                return;
            }

            await docker.DestroyAsync(containerId, context.CancellationToken);

            context.AddExecutionLogEntry("ContainerDestroyed",
                $"Container {containerId[..Math.Min(12, containerId.Length)]} removed");

            await context.CompleteActivityWithOutcomesAsync("Done");
        }
        catch (Exception ex)
        {
            context.AddExecutionLogEntry("ContainerDestroyFailed", ex.Message);
            await context.CompleteActivityWithOutcomesAsync("Failed");
        }
    }
}
