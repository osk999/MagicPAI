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
        var guiPortAllocator = context.GetService<IGuiPortAllocator>();
        var sessionRegistry = context.GetService<ISessionContainerRegistry>();
        var logStreamer = context.GetService<ISessionContainerLogStreamer>();
        var ownerId = context.WorkflowExecutionContext.Id;
        var containerId = ContainerId.GetOrDefault(context, () => "") ?? "";
        if (string.IsNullOrEmpty(containerId))
            containerId = context.GetVariable<string>("ContainerId") ?? "";
        if (string.IsNullOrEmpty(containerId))
            containerId = context.GetOptionalWorkflowInput<string>("ContainerId") ?? "";

        try
        {
            if (string.IsNullOrEmpty(containerId))
            {
                if (logStreamer is not null)
                    await logStreamer.StopStreamingAsync(context.WorkflowExecutionContext.Id);
                guiPortAllocator?.Release(ownerId);
                sessionRegistry?.UpdateContainer(context.WorkflowExecutionContext.Id, null);
                context.AddExecutionLogEntry("ContainerDestroySkipped", "No container ID provided");
                await context.CompleteActivityWithOutcomesAsync("Done");
                return;
            }

            await docker.DestroyAsync(containerId, context.CancellationToken);
            if (logStreamer is not null)
                await logStreamer.StopStreamingAsync(context.WorkflowExecutionContext.Id);
            guiPortAllocator?.Release(ownerId);
            sessionRegistry?.UpdateContainer(context.WorkflowExecutionContext.Id, null);

            context.AddExecutionLogEntry("ContainerDestroyed",
                $"Container {containerId[..Math.Min(12, containerId.Length)]} removed");

            await context.CompleteActivityWithOutcomesAsync("Done");
        }
        catch (Exception ex)
        {
            guiPortAllocator?.Release(ownerId);
            context.AddExecutionLogEntry("ContainerDestroyFailed", ex.Message);
            await context.CompleteActivityWithOutcomesAsync("Failed");
        }
    }
}
