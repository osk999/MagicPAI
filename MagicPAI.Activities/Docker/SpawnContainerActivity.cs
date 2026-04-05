using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Attributes;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;
using Elsa.Workflows.UIHints;
using MagicPAI.Core.Models;
using MagicPAI.Core.Services;

namespace MagicPAI.Activities.Docker;

[Activity("MagicPAI", "Docker",
    "Spawn an isolated Docker container for task execution")]
[FlowNode("Done", "Failed")]
public class SpawnContainerActivity : Activity
{
    [Input(DisplayName = "Image")]
    public Input<string> Image { get; set; } = new("magicpai-env:latest");

    [Input(DisplayName = "Workspace Path",
        Description = "Host path to mount as /workspace")]
    public Input<string> WorkspacePath { get; set; } = default!;

    [Input(DisplayName = "Memory Limit (MB)", Category = "Resources")]
    public Input<int> MemoryLimitMb { get; set; } = new(4096);

    [Input(DisplayName = "Enable GUI (noVNC)", Category = "Features")]
    public Input<bool> EnableGui { get; set; } = new(false);

    [Input(DisplayName = "Environment Variables",
        UIHint = InputUIHints.JsonEditor, Category = "Advanced")]
    public Input<Dictionary<string, string>?> EnvVars { get; set; } = default!;

    [Output(DisplayName = "Container ID")]
    public Output<string> ContainerId { get; set; } = default!;

    [Output(DisplayName = "GUI URL")]
    public Output<string?> GuiUrl { get; set; } = default!;

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var docker = context.GetRequiredService<IContainerManager>();

        var config = new ContainerConfig
        {
            Image = Image.Get(context),
            WorkspacePath = WorkspacePath.Get(context),
            MemoryLimitMb = MemoryLimitMb.Get(context),
            EnableGui = EnableGui.Get(context),
            Env = EnvVars.GetOrDefault(context, () => null) ?? new Dictionary<string, string>()
        };

        try
        {
            var result = await docker.SpawnAsync(config, context.CancellationToken);

            ContainerId.Set(context, result.ContainerId);
            GuiUrl.Set(context, result.GuiUrl);

            context.AddExecutionLogEntry("ContainerSpawned",
                $"Container {result.ContainerId[..12]} started");

            await context.CompleteActivityWithOutcomesAsync("Done");
        }
        catch (Exception ex)
        {
            context.AddExecutionLogEntry("ContainerSpawnFailed", ex.Message);
            await context.CompleteActivityWithOutcomesAsync("Failed");
        }
    }
}
