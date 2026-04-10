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

[Activity("MagicPAI", "AI Agents", "Determine whether a task should route into website audit")]
[FlowNode("Website", "NonWebsite")]
public class WebsiteTaskClassifierActivity : Activity
{
    [Input(DisplayName = "Prompt", UIHint = InputUIHints.MultiLine)]
    public Input<string> Prompt { get; set; } = default!;

    [Input(DisplayName = "Container ID")]
    public Input<string> ContainerId { get; set; } = default!;

    [Output(DisplayName = "Is Website Task")]
    public Output<bool> IsWebsiteTask { get; set; } = default!;

    [Output(DisplayName = "Confidence")]
    public Output<int> Confidence { get; set; } = default!;

    [Output(DisplayName = "Rationale")]
    public Output<string> Rationale { get; set; } = default!;

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var containerMgr = context.GetRequiredService<IContainerManager>();
        var agentFactory = context.GetRequiredService<ICliAgentFactory>();
        var config = context.GetRequiredService<MagicPaiConfig>();

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
            var request = new AgentRequest
            {
                Prompt = BuildWebsitePrompt(prompt),
                Model = AiAssistantResolver.ResolveModelForPower(runner, config, 3),
                OutputSchema = SchemaGenerator.FromType<WebsiteTaskClassificationResult>(),
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
                throw new InvalidOperationException("Container ID is required. Website routing classification runs inside the spawned worker container.");

            foreach (var setupRequest in plan.SetupRequests ?? [])
                await containerMgr.ExecAsync(cid, setupRequest, context.CancellationToken);

            var result = await containerMgr.ExecAsync(cid, plan.MainRequest, context.CancellationToken);
            if (result.ExitCode != 0)
            {
                context.AddExecutionLogEntry("WebsiteTaskClassificationFailed",
                    JsonSerializer.Serialize(new
                    {
                        exitCode = result.ExitCode,
                        output = Truncate(result.Output),
                        error = Truncate(result.Error)
                    }));
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(result.Error)
                        ? $"Website classifier exited with code {result.ExitCode}."
                        : result.Error);
            }

            var parsedResponse = runner.ParseResponse(result.Output ?? "");
            if (!string.IsNullOrWhiteSpace(parsedResponse.SessionId))
                AssistantSessionState.SetSessionId(context, assistantName, parsedResponse.SessionId!);

            var parsed = ParseResponse(parsedResponse.Output ?? result.Output ?? "");
            IsWebsiteTask.Set(context, parsed.IsWebsiteTask);
            Confidence.Set(context, parsed.Confidence);
            Rationale.Set(context, parsed.Rationale);

            context.AddExecutionLogEntry("WebsiteTaskResult",
                JsonSerializer.Serialize(new
                {
                    verdict = parsed.IsWebsiteTask ? "Website" : "NonWebsite",
                    isWebsiteTask = parsed.IsWebsiteTask,
                    confidence = parsed.Confidence,
                    rationale = parsed.Rationale
                }));

            await context.CompleteActivityWithOutcomesAsync(parsed.IsWebsiteTask ? "Website" : "NonWebsite");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            context.AddExecutionLogEntry("WebsiteTaskClassificationFailed", ex.ToString());
            throw;
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

    private static string BuildWebsitePrompt(string userPrompt) =>
        $$"""
        Decide whether this task should route into a browser-based website audit workflow.
        A website task includes UI/UX audit, browser automation, frontend interaction review, layout/accessibility review, or website quality assessment.
        A non-website task includes backend, API, infrastructure, CLI, library, desktop, or general code changes even if they are complex.
        Respond with JSON only:
        {
          "isWebsiteTask": <true|false>,
          "confidence": <1-10>,
          "rationale": "<short explanation>"
        }

        Task: {{userPrompt}}
        """;

    private static WebsiteTaskClassificationResult ParseResponse(string output)
    {
        try
        {
            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;
            return new WebsiteTaskClassificationResult(
                IsWebsiteTask: root.TryGetProperty("isWebsiteTask", out var website) && website.GetBoolean(),
                Confidence: root.TryGetProperty("confidence", out var confidence) ? confidence.GetInt32() : 5,
                Rationale: root.TryGetProperty("rationale", out var rationale) ? rationale.GetString() ?? "" : "");
        }
        catch
        {
            return new WebsiteTaskClassificationResult(false, 1, "Classifier output could not be parsed.");
        }
    }

    private static string Truncate(string? value, int maxLength = 4000)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
