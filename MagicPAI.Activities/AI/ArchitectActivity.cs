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
using Microsoft.Extensions.Logging;

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
        var config = context.GetRequiredService<MagicPaiConfig>();
        var logger = context.GetRequiredService<ILogger<ArchitectActivity>>();

        try
        {
            if (config.RequireContainerizedAgentExecution && !config.UseWorkerContainers)
                throw new InvalidOperationException("AI agent execution is configured to run only inside worker containers, but no worker-container backend is enabled.");

            var assistantName = AiAssistantResolver.NormalizeAssistant(
                context.GetOptionalWorkflowInput<string>("AiAssistant")
                ?? context.GetOptionalWorkflowInput<string>("Agent")
                ?? config.DefaultAgent,
                config.DefaultAgent);
            var runner = agentFactory.Create(assistantName);
            var prompt = context.GetOptionalWorkflowInput<string>("Prompt") ?? Prompt.Get(context) ?? "";
            var workDir = config.UseWorkerContainers
                ? config.ContainerWorkDir ?? "/workspace"
                : context.GetOptionalWorkflowInput<string>("WorkspacePath")
                    ?? config.WorkspacePath
                    ?? ".";
            var gapContext = GapContext.GetOrDefault(context, () => null);

            var architectPrompt = BuildArchitectPrompt(prompt, gapContext);
            var request = new AgentRequest
            {
                Prompt = architectPrompt,
                Model = AiAssistantResolver.ResolveModelForPower(runner, config, 1),
                WorkDir = workDir,
                SessionId = AssistantSessionState.GetOrCreateSessionId(context, assistantName)
            };
            var plan = runner.BuildExecutionPlan(request);

            var cid = ContainerId.GetOrDefault(context, () => "");
            if (string.IsNullOrEmpty(cid))
                cid = TryGetVariable<string>(context, "ContainerId") ?? "";
            if (string.IsNullOrEmpty(cid))
                cid = context.GetOptionalWorkflowInput<string>("ContainerId") ?? "";
            if (string.IsNullOrWhiteSpace(cid))
                throw new InvalidOperationException("Container ID is required. Architect runs inside the spawned worker container.");

            foreach (var setupRequest in plan.SetupRequests ?? [])
                await containerMgr.ExecAsync(cid, setupRequest, context.CancellationToken);

            var result = await containerMgr.ExecAsync(
                cid, plan.MainRequest, context.CancellationToken);

            context.AddExecutionLogEntry("ArchitectCommandResult",
                JsonSerializer.Serialize(new
                {
                    exitCode = result.ExitCode,
                    output = Truncate(result.Output),
                    error = Truncate(result.Error)
                }));

            if (result.ExitCode != 0)
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(result.Error)
                        ? $"Architect agent exited with code {result.ExitCode}."
                        : result.Error);

            var parsed = runner.ParseResponse(result.Output ?? "");
            if (!string.IsNullOrWhiteSpace(parsed.SessionId))
                AssistantSessionState.SetSessionId(context, assistantName, parsed.SessionId!);
            var tasks = ParseTaskList(parsed.Output ?? "");

            TaskListJson.Set(context, tasks);
            TaskCount.Set(context, tasks.Length);

            context.AddExecutionLogEntry("ArchitectResult",
                JsonSerializer.Serialize(new
                {
                    taskCount = tasks.Length,
                    tasks
                }));

            await context.CompleteActivityWithOutcomesAsync(
                tasks.Length > 0 ? "Done" : "Failed");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Architect activity failed");
            var fallbackPrompt = context.GetOptionalWorkflowInput<string>("Prompt") ?? Prompt.Get(context) ?? "";
            var fallbackTasks = string.IsNullOrWhiteSpace(fallbackPrompt)
                ? Array.Empty<string>()
                : new[] { fallbackPrompt };

            context.AddExecutionLogEntry("ArchitectFailed", ex.ToString());
            context.AddExecutionLogEntry("ArchitectFallback",
                $"Using fallback decomposition with {fallbackTasks.Length} task(s).");
            TaskListJson.Set(context, fallbackTasks);
            TaskCount.Set(context, fallbackTasks.Length);
            await context.CompleteActivityWithOutcomesAsync(
                fallbackTasks.Length > 0 ? "Done" : "Failed");
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

    private static string Truncate(string? value, int maxLength = 4000)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
