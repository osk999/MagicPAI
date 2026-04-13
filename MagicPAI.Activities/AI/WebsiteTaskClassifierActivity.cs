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

[Activity("MagicPAI", "AI Agents", "Determine whether a task should route into website audit",
    Kind = ActivityKind.Task,
    RunAsynchronously = true)]
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
            if (TryHeuristicClassification(prompt) is { } heuristicClassification)
            {
                EmitResult(context, heuristicClassification, "WebsiteTaskHeuristicOverride");
                await context.CompleteActivityWithOutcomesAsync(heuristicClassification.IsWebsiteTask ? "Website" : "NonWebsite");
                return;
            }

            var workDir = config.UseWorkerContainers
                ? config.ContainerWorkDir ?? "/workspace"
                : context.GetOptionalWorkflowInput<string>("WorkspacePath")
                    ?? config.WorkspacePath
                    ?? ".";
            var request = new AgentRequest
            {
                Prompt = BuildWebsitePrompt(prompt),
                Model = AiAssistantResolver.ResolveModelForPower(runner, config, 1),
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
            if (!parsed.IsWebsiteTask && TryHeuristicClassification(prompt) is { } overrideClassification)
            {
                var rationale = string.IsNullOrWhiteSpace(parsed.Rationale)
                    ? overrideClassification.Rationale
                    : $"{overrideClassification.Rationale} Model rationale: {parsed.Rationale}";
                parsed = overrideClassification with { Rationale = rationale };
                context.AddExecutionLogEntry("WebsiteTaskHeuristicOverride", rationale);
            }

            EmitResult(context, parsed);

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
        
        Route to WEBSITE AUDIT (true) when ALL of the following apply:
        1. The project is a website or web application that runs in a browser, or the requested work is clearly browser-visible frontend work.
        2. The task involves ANY of: auditing, testing, verifying, QA, reviewing, or fixing/improving CSS, UI, UX, styling, layout, responsiveness, accessibility, forms, navigation, or other browser-visible behavior.
        3. A browser should be used to confirm the result, even if the URL is only implied by the project type or working directory.
        
        Route to STANDARD PIPELINE (false) when ANY of the following apply:
        - The task is backend, API, infrastructure, CLI, library, data, or desktop work with no browser verification needed.
        - The requested change is non-visual refactoring or implementation that does not need a browser to confirm.
        - The project does not expose a browser-accessible UI and no browser-visible behavior is in scope.
        
        Signals that strongly suggest WEBSITE AUDIT:
        - Words like audit, test, verify, QA, review the site, review the app, visual regression, accessibility, responsive, mobile, layout, design, styling, CSS, UI, or UX.
        - The task asks to change how a page looks or behaves in the browser.
        - Input fields, buttons, forms, navigation, scrolling, or interactions must be checked or fixed.
        - The project or prompt implies a browser app such as ASP.NET, Blazor, React, Next.js, Vue, Angular, Django, Rails, or another website stack.
        - The user wants browser-visible issues found AND fixed.
        
        IMPORTANT: CSS/UI/UX fix tasks ALWAYS route to WEBSITE AUDIT (true). A request like "change the CSS", "redesign the page", or "fix the UI" is a website-audit task even if it also requires code changes.
        
        Respond with JSON only:
        {
          "isWebsiteTask": <true|false>,
          "confidence": <1-10>,
          "rationale": "<short explanation>"
        }

        Task: {{userPrompt}}
        """;

    private static WebsiteTaskClassificationResult? TryHeuristicClassification(string userPrompt)
    {
        if (string.IsNullOrWhiteSpace(userPrompt))
            return null;

        var normalized = userPrompt.ToLowerInvariant();
        var explicitCssTask = ContainsAny(normalized,
            "change the entire css",
            "fix the css",
            "change the css",
            "redesign",
            "fix the ui",
            "change the ui",
            "improve the ui",
            "improve the ux",
            "frontend redesign");
        var hasVisualSurface = ContainsAny(normalized,
            "css",
            "style",
            "styling",
            "ui",
            "ux",
            "layout",
            "responsive",
            "visual",
            "frontend",
            "browser",
            "page",
            "website",
            "web app",
            "web application",
            "font",
            "fonts",
            "color",
            "colors",
            "accessibility",
            "form",
            "button",
            "navigation",
            "scroll",
            "mobile");
        var hasAuditAction = ContainsAny(normalized,
            "audit",
            "test",
            "verify",
            "qa",
            "review",
            "check",
            "fix",
            "improve",
            "change",
            "redesign",
            "look different");

        if (!explicitCssTask && !(hasVisualSurface && hasAuditAction))
            return null;

        return new WebsiteTaskClassificationResult(
            true,
            10,
            "Heuristic website routing override matched browser-visible CSS/UI/UX/frontend task signals.");
    }

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

    private static bool ContainsAny(string value, params string[] candidates) =>
        candidates.Any(candidate => value.Contains(candidate, StringComparison.Ordinal));

    private void EmitResult(ActivityExecutionContext context, WebsiteTaskClassificationResult parsed, string? extraEventName = null)
    {
        IsWebsiteTask.Set(context, parsed.IsWebsiteTask);
        Confidence.Set(context, parsed.Confidence);
        Rationale.Set(context, parsed.Rationale);

        if (!string.IsNullOrWhiteSpace(extraEventName))
            context.AddExecutionLogEntry(extraEventName!, parsed.Rationale);

        context.AddExecutionLogEntry("WebsiteTaskResult",
            JsonSerializer.Serialize(new
            {
                verdict = parsed.IsWebsiteTask ? "Website" : "NonWebsite",
                isWebsiteTask = parsed.IsWebsiteTask,
                confidence = parsed.Confidence,
                rationale = parsed.Rationale
            }));
    }
}
