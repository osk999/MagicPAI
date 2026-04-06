using System.Text.Json;
using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Attributes;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;
using Elsa.Workflows.UIHints;
using MagicPAI.Core.Services;

namespace MagicPAI.Activities.AI;

[Activity("MagicPAI", "AI Agents",
    "Execute a prompt via an AI CLI agent (Claude, Codex, Gemini) in a Docker container")]
[FlowNode("Done", "Failed")]
public class RunCliAgentActivity : Activity
{
    [Input(DisplayName = "Agent",
        UIHint = InputUIHints.DropDown,
        Options = new[] { "claude", "codex", "gemini" },
        Description = "Which AI CLI agent to use")]
    public Input<string> Agent { get; set; } = new("claude");

    [Input(DisplayName = "Prompt", UIHint = InputUIHints.MultiLine)]
    public Input<string> Prompt { get; set; } = default!;

    [Input(DisplayName = "Container ID",
        Description = "Docker container to execute in")]
    public Input<string> ContainerId { get; set; } = default!;

    [Input(DisplayName = "Working Directory")]
    public Input<string> WorkingDirectory { get; set; } = new("/workspace");

    [Input(DisplayName = "Model",
        UIHint = InputUIHints.DropDown,
        Options = new[] { "auto", "haiku", "sonnet", "opus",
                          "gpt-5.4", "gpt-5.4-mini", "gpt-5.3-codex",
                          "gemini-3.1-pro-preview", "gemini-3-flash" },
        Category = "Model")]
    public Input<string> Model { get; set; } = new("auto");

    [Input(DisplayName = "Max Turns", Category = "Limits")]
    public Input<int> MaxTurns { get; set; } = new(20);

    [Input(DisplayName = "Timeout (minutes)", Category = "Limits")]
    public Input<int> TimeoutMinutes { get; set; } = new(30);

    [Output(DisplayName = "Response")]
    public Output<string> Response { get; set; } = default!;

    [Output(DisplayName = "Success")]
    public Output<bool> Success { get; set; } = default!;

    [Output(DisplayName = "Cost USD")]
    public Output<decimal> CostUsd { get; set; } = default!;

    [Output(DisplayName = "Files Modified")]
    public Output<string[]> FilesModified { get; set; } = default!;

    [Output(DisplayName = "Exit Code")]
    public Output<int> ExitCode { get; set; } = default!;

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var containerMgr = context.GetRequiredService<IContainerManager>();
        var agentFactory = context.GetRequiredService<ICliAgentFactory>();

        try
        {
            // Read containerId from activity input, falling back to workflow variable
            var containerId = ContainerId.Get(context);
            if (string.IsNullOrEmpty(containerId))
                containerId = context.GetVariable<string>("ContainerId") ?? "";
            var agentName = context.GetWorkflowInput<string>("Agent") ?? Agent.Get(context) ?? "claude";
            var agent = agentFactory.Create(agentName);
            var prompt = context.GetWorkflowInput<string>("Prompt") ?? Prompt.Get(context) ?? "";
            var model = context.GetWorkflowInput<string>("Model") ?? Model.Get(context) ?? "auto";
            var workDir = context.GetWorkflowInput<string>("WorkspacePath") ?? WorkingDirectory.Get(context) ?? "/workspace";
            var maxTurns = MaxTurns.Get(context);

            var command = agent.BuildCommand(new AgentRequest
            {
                Prompt = prompt,
                Model = model == "auto" ? null : model,
                WorkDir = workDir
            });

            var result = await containerMgr.ExecStreamingAsync(
                containerId, command,
                onOutput: chunk =>
                {
                    context.AddExecutionLogEntry("OutputChunk",
                        JsonSerializer.Serialize(new
                        {
                            activityId = context.Activity.Id,
                            text = chunk
                        }));
                },
                timeout: TimeSpan.FromMinutes(TimeoutMinutes.Get(context)),
                ct: context.CancellationToken);

            var parsed = agent.ParseResponse(result.Output ?? "");

            Response.Set(context, parsed.Output);
            Success.Set(context, parsed.Success);
            CostUsd.Set(context, parsed.CostUsd);
            FilesModified.Set(context, parsed.FilesModified);
            ExitCode.Set(context, result.ExitCode);

            await context.CompleteActivityWithOutcomesAsync(
                parsed.Success ? "Done" : "Failed");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            context.AddExecutionLogEntry("AgentExecutionFailed", ex.Message);
            Response.Set(context, ex.Message);
            Success.Set(context, false);
            ExitCode.Set(context, -1);
            await context.CompleteActivityWithOutcomesAsync("Failed");
        }
    }
}
