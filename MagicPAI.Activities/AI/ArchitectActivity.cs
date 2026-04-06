using System.Text.Json;
using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Attributes;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;
using Elsa.Workflows.UIHints;
using MagicPAI.Core.Services;

namespace MagicPAI.Activities.AI;

[Activity("MagicPAI", "AI Agents", "Decompose a complex prompt into sub-tasks")]
[FlowNode("Done", "Failed")]
public class ArchitectActivity : Activity
{
    [Input(DisplayName = "Prompt", UIHint = InputUIHints.MultiLine)]
    public Input<string> Prompt { get; set; } = default!;

    [Input(DisplayName = "Container ID")]
    public Input<string> ContainerId { get; set; } = default!;

    [Input(DisplayName = "Gap Context",
        UIHint = InputUIHints.MultiLine,
        Description = "Additional context about codebase gaps or structure",
        Category = "Context")]
    public Input<string?> GapContext { get; set; } = default!;

    [Output(DisplayName = "Task List JSON")]
    public Output<string[]> TaskListJson { get; set; } = default!;

    [Output(DisplayName = "Task Count")]
    public Output<int> TaskCount { get; set; } = default!;

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var containerMgr = context.GetRequiredService<IContainerManager>();
        var agentFactory = context.GetRequiredService<ICliAgentFactory>();

        try
        {
            var runner = agentFactory.Create("claude");
            var prompt = context.GetWorkflowInput<string>("Prompt") ?? Prompt.Get(context) ?? "";
            var workDir = context.GetWorkflowInput<string>("WorkspacePath") ?? "/workspace";
            var gapContext = GapContext.GetOrDefault(context, () => null);

            var architectPrompt = BuildArchitectPrompt(prompt, gapContext);
            var command = runner.BuildCommand(new AgentRequest
            {
                Prompt = architectPrompt,
                Model = "opus",
                WorkDir = workDir
            });

            var cid = ContainerId.Get(context);
            if (string.IsNullOrEmpty(cid))
                cid = context.GetVariable<string>("ContainerId") ?? "";

            var result = await containerMgr.ExecAsync(
                cid, command, workDir, context.CancellationToken);

            var parsed = runner.ParseResponse(result.Output ?? "");
            var tasks = ParseTaskList(parsed.Output ?? "");

            TaskListJson.Set(context, tasks);
            TaskCount.Set(context, tasks.Length);

            context.AddExecutionLogEntry("ArchitectResult",
                $"Decomposed into {tasks.Length} sub-tasks");

            await context.CompleteActivityWithOutcomesAsync(
                tasks.Length > 0 ? "Done" : "Failed");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            context.AddExecutionLogEntry("ArchitectFailed", ex.Message);
            TaskListJson.Set(context, []);
            TaskCount.Set(context, 0);
            await context.CompleteActivityWithOutcomesAsync("Failed");
        }
    }

    private static string BuildArchitectPrompt(string userPrompt, string? gapContext)
    {
        var contextSection = string.IsNullOrEmpty(gapContext)
            ? ""
            : $"\n\nCodebase context:\n{gapContext}";

        return $"""
            You are a software architect. Decompose this task into independent sub-tasks.
            Each sub-task should be completable by a single AI coding agent.
            Respond with a JSON array of task descriptions (strings only).

            Example: ["Implement the User model", "Add validation logic", "Write unit tests"]
            {contextSection}

            Task: {userPrompt}
            """;
    }

    private static string[] ParseTaskList(string output)
    {
        try
        {
            // Try to extract JSON array from the response
            var trimmed = output.Trim();
            var startIdx = trimmed.IndexOf('[');
            var endIdx = trimmed.LastIndexOf(']');
            if (startIdx >= 0 && endIdx > startIdx)
            {
                var json = trimmed[startIdx..(endIdx + 1)];
                return JsonSerializer.Deserialize<string[]>(json) ?? [];
            }
            return [];
        }
        catch
        {
            return [];
        }
    }
}
