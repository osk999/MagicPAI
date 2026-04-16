using System.Text;
using System.Text.Json;
using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Attributes;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;
using Elsa.Workflows.UIHints;
using MagicPAI.Core.Models;
using MagicPAI.Core.Services;
using Microsoft.Extensions.Logging;

namespace MagicPAI.Activities.AI;

/// <summary>
/// Requirements-coverage classifier. Runs AFTER the worker agent and verification
/// gates, and grades the completed work against the original user requirements.
/// Emits <c>AllMet</c> when every requirement is satisfied; otherwise emits
/// <c>Incomplete</c> with a concrete gap prompt wired into the
/// <c>RepairPrompt</c> variable so the run-agent step can be re-invoked. When
/// the iteration cap is reached it emits <c>Exceeded</c> so the workflow can
/// still finish cleanly.
/// </summary>
[Activity("MagicPAI", "Verification",
    "Grade completed work against the original requirements; loop back on gaps",
    Kind = ActivityKind.Task,
    RunAsynchronously = true)]
[FlowNode("AllMet", "Incomplete", "Exceeded")]
public class RequirementsCoverageActivity : Activity
{
    [Input(DisplayName = "Original Prompt", UIHint = InputUIHints.MultiLine,
        Description = "The user's original requirements/prompt (falls back to workflow 'Prompt' input).")]
    public Input<string> OriginalPrompt { get; set; } = new("");

    [Input(DisplayName = "Container ID")]
    public Input<string> ContainerId { get; set; } = new("");

    [Input(DisplayName = "Working Directory")]
    public Input<string> WorkingDirectory { get; set; } = new("/workspace");

    [Input(DisplayName = "Max Iterations",
        Description = "How many times the workflow may loop back to the agent before giving up.",
        Category = "Limits")]
    public Input<int> MaxIterations { get; set; } = new(30);

    [Input(DisplayName = "Model Power",
        Description = "1 = strongest, 2 = balanced. Classifier is cheap — 2 is usually enough.",
        Category = "Model")]
    public Input<int> ModelPower { get; set; } = new(2);

    [Output(DisplayName = "All Met")]
    public Output<bool> AllMet { get; set; } = default!;

    [Output(DisplayName = "Gap Prompt")]
    public Output<string> GapPrompt { get; set; } = default!;

    [Output(DisplayName = "Coverage Report JSON")]
    public Output<string> CoverageReportJson { get; set; } = default!;

    [Output(DisplayName = "Iteration")]
    public Output<int> Iteration { get; set; } = default!;

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var logger = context.GetRequiredService<ILogger<RequirementsCoverageActivity>>();

        try
        {
            var containerId = ActivityHelpers.ResolveContainerId(ContainerId, context);
            var originalPrompt = ActivityHelpers.FirstNonEmpty(
                ActivityHelpers.Optional(OriginalPrompt, context),
                ActivityHelpers.TryGetVariable<string>(context, "OriginalPrompt"),
                context.GetOptionalWorkflowInput<string>("Prompt"),
                ActivityHelpers.TryGetVariable<string>(context, "Prompt"),
                "") ?? "";

            if (string.IsNullOrWhiteSpace(originalPrompt))
            {
                context.AddExecutionLogEntry("CoverageSkipped",
                    "No original prompt available — skipping coverage check.");
                AllMet.Set(context, true);
                GapPrompt.Set(context, "");
                CoverageReportJson.Set(context, "{}");
                Iteration.Set(context, 0);
                await context.CompleteActivityWithOutcomesAsync("AllMet");
                return;
            }

            var maxIterations = Math.Max(1, ActivityHelpers.GetOrDefault(MaxIterations, context, 30));
            var currentIteration = ActivityHelpers.TryGetVariable<int>(context, "CoverageIterations");

            if (currentIteration >= maxIterations)
            {
                context.AddExecutionLogEntry("CoverageExceeded",
                    $"Coverage loop exceeded {maxIterations} iterations — giving up.");
                AllMet.Set(context, false);
                GapPrompt.Set(context, "");
                CoverageReportJson.Set(context, "{}");
                Iteration.Set(context, currentIteration);
                await context.CompleteActivityWithOutcomesAsync("Exceeded");
                return;
            }

            var nextIteration = currentIteration + 1;
            context.SetVariable("CoverageIterations", nextIteration);

            var workDir = ActivityHelpers.GetOrDefault(WorkingDirectory, context, "/workspace");
            var modelPower = ActivityHelpers.GetOrDefault(ModelPower, context, 2);

            var evidence = await CollectEvidenceAsync(context, containerId, workDir);
            var classifierPrompt = BuildClassifierPrompt(originalPrompt, evidence, nextIteration, maxIterations);
            var schema = SchemaGenerator.FromType<CoverageResult>();

            var result = await AiCliExecutor.ExecuteAsync(context,
                new AiCliExecutor.ExecutionParams
                {
                    ContainerId = containerId,
                    Prompt = classifierPrompt,
                    ModelPower = modelPower,
                    OutputSchema = schema,
                    UseStreaming = false,
                    MaxRetries = 2,
                    TimeoutMinutes = 10,
                    WorkDir = workDir
                });

            var parsed = ParseCoverageResult(result.StructuredOutputJson ?? result.Response);
            var reportJson = result.StructuredOutputJson
                ?? JsonSerializer.Serialize(parsed);

            AllMet.Set(context, parsed.AllMet);
            GapPrompt.Set(context, parsed.GapPrompt ?? "");
            CoverageReportJson.Set(context, reportJson);
            Iteration.Set(context, nextIteration);

            var missing = parsed.Requirements
                .Where(r => !string.Equals(r.Status, "done", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            logger.LogInformation(
                "[Coverage] iter {Iter}/{Max} allMet={AllMet} missing={Missing} summary=\"{Summary}\"",
                nextIteration, maxIterations, parsed.AllMet,
                string.Join(",", missing.Select(m => $"{m.Id}:{m.Status}")),
                Truncate(parsed.Summary ?? "", 160));

            context.AddExecutionLogEntry("CoverageVerdict",
                JsonSerializer.Serialize(new
                {
                    iteration = nextIteration,
                    maxIterations,
                    allMet = parsed.AllMet,
                    missingCount = missing.Length,
                    gapPrompt = parsed.GapPrompt,
                    summary = parsed.Summary
                }));

            if (parsed.AllMet)
            {
                await context.CompleteActivityWithOutcomesAsync("AllMet");
                return;
            }

            // Feed the gap prompt into the same variable RunCliAgent/AiAssistant
            // activities read their prompt from (ResolvePrompt: input -> RepairPrompt
            // -> Prompt -> workflow input). Setting RepairPrompt causes the next
            // run-agent to use this focused follow-up without re-running research.
            var feedbackPrompt = BuildFeedbackPrompt(originalPrompt, parsed, nextIteration, maxIterations);
            context.SetVariable("RepairPrompt", feedbackPrompt);
            context.SetVariable("LastCoverageGap", parsed.GapPrompt ?? "");

            await context.CompleteActivityWithOutcomesAsync("Incomplete");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            context.AddExecutionLogEntry("CoverageError", ex.ToString());
            logger.LogError(ex, "Requirements coverage classifier failed — defaulting to AllMet so the workflow can finish.");
            AllMet.Set(context, true);
            GapPrompt.Set(context, "");
            CoverageReportJson.Set(context, "{}");
            Iteration.Set(context, ActivityHelpers.TryGetVariable<int>(context, "CoverageIterations"));
            await context.CompleteActivityWithOutcomesAsync("AllMet");
        }
    }

    /// <summary>
    /// Collect quick evidence of what the worker did: git status + git diff stat.
    /// The classifier uses this plus the last agent response (already on the
    /// resumed Claude session) to grade each requirement.
    /// </summary>
    private static async Task<string> CollectEvidenceAsync(
        ActivityExecutionContext context, string containerId, string workDir)
    {
        var containerMgr = context.GetRequiredService<IContainerManager>();
        var sb = new StringBuilder();

        var diffStat = await RunBashAsync(containerMgr, containerId, workDir,
            "git diff --stat 2>/dev/null | tail -40");
        sb.AppendLine("## git diff --stat");
        sb.AppendLine(TruncateBlock(diffStat, 3000));
        sb.AppendLine();

        var status = await RunBashAsync(containerMgr, containerId, workDir,
            "git status --short 2>/dev/null | head -80");
        sb.AppendLine("## git status --short");
        sb.AppendLine(TruncateBlock(status, 2000));
        sb.AppendLine();

        var screenshots = await RunBashAsync(containerMgr, containerId, workDir,
            "ls -la /workspace/screenshots/ 2>/dev/null | tail -30");
        if (!string.IsNullOrWhiteSpace(screenshots))
        {
            sb.AppendLine("## screenshots");
            sb.AppendLine(TruncateBlock(screenshots, 2000));
            sb.AppendLine();
        }

        var lastAgent = ActivityHelpers.TryGetVariable<string>(context, "LastAgentResponse")
            ?? ActivityHelpers.TryGetVariable<string>(context, "WorkerOutput")
            ?? "";
        if (!string.IsNullOrWhiteSpace(lastAgent))
        {
            sb.AppendLine("## last agent response (truncated)");
            sb.AppendLine(Truncate(lastAgent, 8000));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static async Task<string> RunBashAsync(
        IContainerManager containerMgr, string containerId, string workDir, string command)
    {
        try
        {
            var result = await containerMgr.ExecAsync(containerId,
                new ContainerExecRequest(
                    FileName: "bash",
                    Arguments: new List<string> { "-lc", command },
                    WorkingDirectory: workDir),
                CancellationToken.None);
            return result.Output ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static string BuildClassifierPrompt(
        string originalPrompt, string evidence, int iteration, int maxIterations)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a REQUIREMENTS COVERAGE CLASSIFIER. Your only job is to grade a completed engineering task");
        sb.AppendLine("against the user's original requirements and report which items are fully done, partially done,");
        sb.AppendLine("or missing.");
        sb.AppendLine();
        sb.AppendLine($"This is coverage iteration {iteration} of {maxIterations}. Keep output tight and concrete.");
        sb.AppendLine();
        sb.AppendLine("Output STRICTLY in the structured JSON schema you've been given.");
        sb.AppendLine();
        sb.AppendLine("RULES:");
        sb.AppendLine("- Each requirement gets status done | partial | missing. Nothing else.");
        sb.AppendLine("- Evidence must be concrete (file paths, endpoints hit, UI elements confirmed, screenshot names).");
        sb.AppendLine("- If a requirement asks for a SPECIFIC concrete action (e.g. 'place a trade and close it')");
        sb.AppendLine("  and only adjacent evidence exists (e.g. 'positions list rendered'), mark PARTIAL and state what's missing.");
        sb.AppendLine("- gap_prompt MUST be a short direct instruction the worker can act on. Empty when all_met=true.");
        sb.AppendLine("- all_met is true only when EVERY requirement is 'done'.");
        sb.AppendLine();
        sb.AppendLine("===== ORIGINAL USER REQUIREMENTS =====");
        sb.AppendLine(originalPrompt);
        sb.AppendLine();
        sb.AppendLine("===== EVIDENCE FROM WORKSPACE =====");
        sb.AppendLine(evidence);
        sb.AppendLine();
        sb.AppendLine("You MAY inspect the codebase with Read / Grep / Glob / Bash to confirm or disconfirm claims before");
        sb.AppendLine("grading. But be efficient — prefer one targeted Read or Grep over broad exploration. The workspace");
        sb.AppendLine("is mounted at /workspace. Do not modify any files.");
        sb.AppendLine();
        sb.AppendLine("Now grade each requirement and return the JSON.");
        return sb.ToString();
    }

    private static string BuildFeedbackPrompt(
        string originalPrompt, CoverageResult verdict, int iteration, int maxIterations)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Requirements coverage check found gaps. Close them.");
        sb.AppendLine();
        sb.AppendLine($"Coverage iteration: {iteration}/{maxIterations}");
        sb.AppendLine();
        sb.AppendLine("## What's still missing or partial");
        foreach (var item in verdict.Requirements)
        {
            if (string.Equals(item.Status, "done", StringComparison.OrdinalIgnoreCase)) continue;
            sb.AppendLine($"- [{item.Status.ToUpperInvariant()}] {item.Id}: {item.Description}");
            if (!string.IsNullOrWhiteSpace(item.Evidence))
                sb.AppendLine($"    evidence: {item.Evidence}");
        }
        sb.AppendLine();
        sb.AppendLine("## Focused follow-up (do exactly this)");
        sb.AppendLine(verdict.GapPrompt);
        sb.AppendLine();
        sb.AppendLine("## Instructions");
        sb.AppendLine("1. Do NOT re-explore the whole codebase — you already have context from the previous pass.");
        sb.AppendLine("2. Touch only what's needed to close the listed gaps. Do not refactor unrelated code.");
        sb.AppendLine("3. Re-verify each closed gap in the running app when relevant (Playwright MCP is ready).");
        sb.AppendLine("4. Take a fresh screenshot per gap you close and save under /workspace/screenshots/.");
        sb.AppendLine("5. When finished, print a one-line summary of what you did this pass.");
        sb.AppendLine();
        sb.AppendLine("## Original Requirements (reference)");
        sb.AppendLine(Truncate(originalPrompt, 4000));
        return sb.ToString();
    }

    private static CoverageResult ParseCoverageResult(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return FailSafe("Classifier returned empty output.");

        try
        {
            var trimmed = output.Trim();
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;

            var items = new List<CoverageItem>();
            if (root.TryGetProperty("requirements", out var reqs) && reqs.ValueKind == JsonValueKind.Array)
            {
                foreach (var r in reqs.EnumerateArray())
                {
                    items.Add(new CoverageItem(
                        Id: GetString(r, "id"),
                        Description: GetString(r, "description"),
                        Status: GetString(r, "status", "missing"),
                        Evidence: GetString(r, "evidence")));
                }
            }

            var allMet = root.TryGetProperty("all_met", out var a) && a.ValueKind == JsonValueKind.True;
            // Defensive: if any item is not done, override all_met to false.
            if (items.Any(i => !string.Equals(i.Status, "done", StringComparison.OrdinalIgnoreCase)))
                allMet = false;

            return new CoverageResult(
                Requirements: items.ToArray(),
                AllMet: allMet,
                GapPrompt: GetString(root, "gap_prompt"),
                Summary: GetString(root, "summary"));
        }
        catch (Exception ex)
        {
            return FailSafe($"Failed to parse classifier output: {ex.Message}");
        }
    }

    private static CoverageResult FailSafe(string reason) =>
        new(Requirements: [],
            AllMet: true, // if the classifier itself fails we don't want to spin-lock the workflow
            GapPrompt: "",
            Summary: reason);

    private static string GetString(JsonElement el, string name, string fallback = "") =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? fallback
            : fallback;

    private static string Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) ? "" : (value.Length <= max ? value : value[..max]);

    private static string TruncateBlock(string value, int max) =>
        string.IsNullOrWhiteSpace(value) ? "(empty)" : Truncate(value, max);
}
