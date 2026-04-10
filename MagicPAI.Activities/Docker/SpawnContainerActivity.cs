using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Attributes;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;
using Elsa.Workflows.UIHints;
using MagicPAI.Activities;
using MagicPAI.Core.Models;
using MagicPAI.Core.Services;
using Microsoft.Extensions.Logging;

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
        var guiPortAllocator = context.GetService<IGuiPortAllocator>();
        var sessionRegistry = context.GetService<ISessionContainerRegistry>();
        var logStreamer = context.GetService<ISessionContainerLogStreamer>();
        var logger = context.GetRequiredService<ILogger<SpawnContainerActivity>>();
        var appConfig = context.GetService<MagicPAI.Core.Config.MagicPaiConfig>();
        var workspacePath = context.GetOptionalWorkflowInput<string>("WorkspacePath");
        if (string.IsNullOrWhiteSpace(workspacePath))
            workspacePath = GetOrDefault(WorkspacePath, context, "");

        var config = new ContainerConfig
        {
            Image = GetOrDefault(Image, context, "magicpai-env:latest"),
            WorkspacePath = workspacePath ?? "",
            MemoryLimitMb = GetOrDefault(MemoryLimitMb, context, 4096),
            EnableGui = GetOrDefault(EnableGui, context, false),
            Env = EnvVars.GetOrDefault(context, () => null) ?? new Dictionary<string, string>()
        };

        var ownerId = context.WorkflowExecutionContext.Id;
        var shouldAllocateGuiPort = config.EnableGui
            && guiPortAllocator is not null
            && appConfig is not null
            && string.Equals(appConfig.ExecutionBackend, "docker", StringComparison.OrdinalIgnoreCase);

        if (shouldAllocateGuiPort)
            config.GuiPort = guiPortAllocator.Reserve(ownerId);

        logger.LogInformation(
            "Spawn container activity starting. WorkspacePath={WorkspacePath} Image={Image}",
            config.WorkspacePath,
            config.Image);
        context.AddExecutionLogEntry(
            "ContainerSpawnStarting",
            $"Starting container for workspace={config.WorkspacePath}");

        try
        {
            var result = await docker.SpawnAsync(config, context.CancellationToken);
            logger.LogInformation("Spawn container activity received container {ContainerId}", result.ContainerId);

            ContainerId.Set(context, result.ContainerId);
            GuiUrl.Set(context, result.GuiUrl);
            logger.LogInformation("Spawn container activity outputs set for {ContainerId}", result.ContainerId);

            // Also set as workflow variable so downstream activities can access it
            context.SetVariable("ContainerId", result.ContainerId);
            sessionRegistry?.UpdateContainer(context.WorkflowExecutionContext.Id, result.ContainerId, result.GuiUrl);
            logStreamer?.StartStreaming(context.WorkflowExecutionContext.Id, result.ContainerId);
            logger.LogInformation("Spawn container activity variable set for {ContainerId}", result.ContainerId);

            context.AddExecutionLogEntry("ContainerSpawned",
                $$"""
                {"containerId":"{{result.ContainerId}}","guiUrl":"{{result.GuiUrl ?? ""}}","workspace":"{{config.WorkspacePath}}"}
                """);
            logger.LogInformation("Spawn container activity completion starting for {ContainerId}", result.ContainerId);

            await context.CompleteActivityWithOutcomesAsync("Done");
            logger.LogInformation("Spawn container activity completed for {ContainerId}", result.ContainerId);
        }
        catch (Exception ex)
        {
            if (shouldAllocateGuiPort)
                guiPortAllocator?.Release(ownerId);

            logger.LogError(ex, "Container spawn failed");
            context.AddExecutionLogEntry("ContainerSpawnFailed", ex.ToString());
            await context.CompleteActivityWithOutcomesAsync("Failed");
        }
    }

    private static T GetOrDefault<T>(Input<T>? input, ActivityExecutionContext context, T fallback)
    {
        if (input is null)
            return fallback;

        try
        {
            return input.Get(context);
        }
        catch (InvalidOperationException)
        {
            return fallback;
        }
    }
}
