using System.Text.Json;
using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Attributes;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;
using Elsa.Workflows.UIHints;
using MagicPAI.Activities;
using MagicPAI.Core.Config;
using MagicPAI.Core.Models;
using MagicPAI.Core.Services;

namespace MagicPAI.Activities.AI;

[Activity("MagicPAI", "AI Agents",
    "Execute a prompt via a generic AI assistant (Claude, Codex, Gemini) in a Docker container")]
[FlowNode("Done", "Failed")]
public class RunCliAgentActivity : Activity
{
    [Input(DisplayName = "AI Assistant",
        UIHint = InputUIHints.DropDown,
        Options = new[] { "claude", "codex", "gemini" },
        Description = "Which AI CLI assistant to use")]
    public Input<string> AiAssistant { get; set; } = new("");

    [Input(DisplayName = "Agent (Legacy)",
        UIHint = InputUIHints.DropDown,
        Options = new[] { "claude", "codex", "gemini" },
        Description = "Legacy alias for AI Assistant")]
    public Input<string> Agent { get; set; } = new("");

    [Input(DisplayName = "Prompt", UIHint = InputUIHints.MultiLine)]
    public Input<string> Prompt { get; set; } = new("");

    [Input(DisplayName = "Container ID",
        Description = "Docker container to execute in")]
    public Input<string> ContainerId { get; set; } = new("");

    [Input(DisplayName = "Working Directory")]
    public Input<string> WorkingDirectory { get; set; } = new("/workspace");

    [Input(DisplayName = "Model",
        UIHint = InputUIHints.DropDown,
        Options = new[] { "auto", "haiku", "sonnet", "opus",
                          "gpt-5.4", "gpt-5.4-mini", "gpt-5.3-codex",
                          "gemini-3.1-pro-preview", "gemini-3-flash" },
        Category = "Model")]
    public Input<string> Model { get; set; } = new("");

    [Input(DisplayName = "Model Power",
        Description = "1 = strongest, 2 = balanced, 3 = fastest. Ignored when Model is set.",
        Category = "Model")]
    public Input<int> ModelPower { get; set; } = new(0);

    [Input(DisplayName = "Structured Output Schema",
        UIHint = InputUIHints.JsonEditor,
        Description = "Optional JSON Schema for structured output",
        Category = "Structured Output")]
    public Input<string> StructuredOutputSchema { get; set; } = new("");

    [Input(DisplayName = "Max Turns", Category = "Limits")]
    public Input<int> MaxTurns { get; set; } = new(20);

    [Input(DisplayName = "Inactivity Timeout (minutes)",
        Description = "Stop the command only if no output is received for this long",
        Category = "Limits")]
    public Input<int> TimeoutMinutes { get; set; } = new(30);

    [Output(DisplayName = "Response")]
    public Output<string> Response { get; set; } = default!;

    [Output(DisplayName = "Structured Output JSON")]
    public Output<string?> StructuredOutputJson { get; set; } = default!;

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
        var config = context.GetRequiredService<MagicPaiConfig>();

        try
        {
            // Read containerId from activity input, falling back to workflow variable
            var containerId = GetOrDefault(ContainerId, context, "");
            if (string.IsNullOrEmpty(containerId))
                containerId = TryGetVariable<string>(context, "ContainerId") ?? "";
            if (string.IsNullOrEmpty(containerId))
                containerId = context.GetOptionalWorkflowInput<string>("ContainerId") ?? "";
            if (config.RequireContainerizedAgentExecution && !config.UseWorkerContainers)
                throw new InvalidOperationException("AI agent execution is configured to run only inside worker containers, but no worker-container backend is enabled.");
            if (string.IsNullOrWhiteSpace(containerId))
                throw new InvalidOperationException("Container ID is required. AI agents execute inside the spawned worker container.");
            var configuredAssistant = FirstNonEmpty(
                Optional(AiAssistant, context),
                context.GetOptionalWorkflowInput<string>("AiAssistant"),
                context.GetOptionalWorkflowInput<string>("Assistant"),
                context.GetOptionalWorkflowInput<string>("Agent"),
                Optional(Agent, context),
                config.DefaultAgent,
                "claude");
            var assistantName = AiAssistantResolver.NormalizeAssistant(configuredAssistant, config.DefaultAgent);
            var agent = agentFactory.Create(assistantName);
            var prompt = FirstNonEmpty(
                Optional(Prompt, context),
                TryGetVariable<string>(context, "RepairPrompt"),
                TryGetVariable<string>(context, "Prompt"),
                context.GetOptionalWorkflowInput<string>("Prompt"),
                "")!;
            var requestedModel = FirstNonEmpty(
                Optional(Model, context),
                context.GetOptionalWorkflowInput<string>("Model"),
                config.DefaultModel,
                "auto");
            var requestedModelPower = FirstNonZero(
                GetOrDefault(ModelPower, context, 0),
                context.GetOptionalWorkflowInput<int?>("ModelPower"));
            var workDir = config.UseWorkerContainers
                ? GetOrDefault(WorkingDirectory, context, config.ContainerWorkDir ?? "/workspace")
                : context.GetOptionalWorkflowInput<string>("WorkspacePath")
                    ?? GetOrDefault(WorkingDirectory, context, "")
                    ?? config.WorkspacePath
                    ?? ".";
            var maxTurns = GetOrDefault(MaxTurns, context, 20);
            var outputSchema = FirstNonEmpty(
                Optional(StructuredOutputSchema, context),
                context.GetOptionalWorkflowInput<string>("StructuredOutputSchema"));
            var resolved = AiAssistantResolver.Resolve(
                agent,
                config,
                assistantName,
                requestedModel,
                requestedModelPower);
            var assistantSessionId = AssistantSessionState.GetOrCreateSessionId(context, resolved.Assistant);

            var request = new AgentRequest
            {
                Prompt = prompt,
                Model = resolved.Model,
                WorkDir = workDir,
                OutputSchema = string.IsNullOrWhiteSpace(outputSchema) ? null : outputSchema,
                SessionId = assistantSessionId
            };
            var plan = agent.BuildExecutionPlan(request);

            context.AddExecutionLogEntry("AiAssistantExecution",
                JsonSerializer.Serialize(new
                {
                    activityId = context.Activity.Id,
                    assistant = resolved.Assistant,
                    model = resolved.Model ?? agent.DefaultModel,
                    modelPower = resolved.ModelPower ?? 0,
                    maxTurns,
                    resumedSessionId = assistantSessionId
                }));

            foreach (var setup in plan.SetupRequests ?? [])
            {
                var setupResult = await containerMgr.ExecAsync(containerId, setup, context.CancellationToken);
                if (setupResult.ExitCode != 0)
                    throw new InvalidOperationException(
                        $"Failed to prepare assistant execution: {setupResult.Error}");
            }

            var result = await containerMgr.ExecStreamingAsync(
                containerId, plan.MainRequest,
                onOutput: chunk =>
                {
                    context.AddExecutionLogEntry("OutputChunk",
                        JsonSerializer.Serialize(new
                        {
                            activityId = context.Activity.Id,
                            text = chunk
                        }));
                },
                timeout: TimeSpan.FromMinutes(GetOrDefault(TimeoutMinutes, context, 30)),
                ct: context.CancellationToken);

            CliAgentResponse parsed;
            try { parsed = agent.ParseResponse(result.Output ?? ""); }
            catch (Exception parseEx)
            {
                context.AddExecutionLogEntry("ParseError", parseEx.Message);
                parsed = new(false, result.Output ?? "", 0, [], 0, 0, null);
            }
            if (!string.IsNullOrWhiteSpace(parsed.SessionId))
                AssistantSessionState.SetSessionId(context, resolved.Assistant, parsed.SessionId!);
            var succeeded = result.ExitCode == 0 && parsed.Success;
            var response = !string.IsNullOrWhiteSpace(parsed.Output)
                ? parsed.Output
                : !string.IsNullOrWhiteSpace(result.Error)
                    ? result.Error
                    : result.Output ?? "";
            var structuredOutput = !string.IsNullOrWhiteSpace(parsed.StructuredOutputJson)
                ? parsed.StructuredOutputJson
                : TryGetJson(response);

            Response.Set(context, response);
            StructuredOutputJson.Set(context, structuredOutput);
            Success.Set(context, succeeded);
            CostUsd.Set(context, parsed.CostUsd);
            FilesModified.Set(context, parsed.FilesModified);
            ExitCode.Set(context, result.ExitCode);
            context.SetVariable("LastAgentResponse", response);
            context.SetVariable("WorkerOutput", response);
            context.SetVariable("LastAgentStructuredOutputJson", structuredOutput ?? "");
            context.SetVariable("LastAgentExitCode", result.ExitCode);
            context.SetVariable("LastAgentSucceeded", succeeded);

            if (string.Equals(context.Activity.Id, "simple-agent", StringComparison.OrdinalIgnoreCase))
                context.SetVariable("SimpleWorkerOutput", response);
            else if (string.Equals(context.Activity.Id, "complex-agent", StringComparison.OrdinalIgnoreCase))
                context.SetVariable("ComplexWorkerOutput", response);
            else if (string.Equals(context.Activity.Id, "repair-agent", StringComparison.OrdinalIgnoreCase))
                context.SetVariable("RepairAgentOutput", response);

            await context.CompleteActivityWithOutcomesAsync(
                succeeded ? "Done" : "Failed");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            context.AddExecutionLogEntry("AgentExecutionFailed", ex.ToString());
            Response.Set(context, ex.Message);
            StructuredOutputJson.Set(context, (string?)null);
            Success.Set(context, false);
            ExitCode.Set(context, -1);
            await context.CompleteActivityWithOutcomesAsync("Failed");
        }
    }

    private static string? Optional(Input<string>? input, ActivityExecutionContext context)
    {
        if (input is null)
            return null;

        string? value;
        try
        {
            value = input.Get(context);
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(value) ? null : value;
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

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static int? FirstNonZero(params int?[] values)
    {
        foreach (var value in values)
        {
            if (value.HasValue && value.Value > 0)
                return value.Value;
        }

        return null;
    }

    private static string? TryGetJson(string output)
    {
        var trimmed = output.Trim();
        if (trimmed.Length == 0)
            return null;
        if (!(trimmed.StartsWith('{') || trimmed.StartsWith('[')))
            return null;

        try
        {
            using var _ = JsonDocument.Parse(trimmed);
            return trimmed;
        }
        catch
        {
            return null;
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
