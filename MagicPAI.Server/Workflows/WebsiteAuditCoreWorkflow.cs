// MagicPAI.Server/Workflows/Temporal/WebsiteAuditCoreWorkflow.cs
// Temporal port of the Elsa WebsiteAuditCoreWorkflow. Audits a single website
// section. Uses a structured-output JSON schema so the parent can reliably
// aggregate per-section results. See temporal.md §H.11.
using System.Text.Json;
using Temporalio.Workflows;
using MagicPAI.Activities.AI;
using MagicPAI.Activities.Contracts;
using MagicPAI.Activities.Docker;
using MagicPAI.Workflows;
using MagicPAI.Workflows.Contracts;

namespace MagicPAI.Server.Workflows;

/// <summary>
/// Runs a single-section website audit. Called by
/// <see cref="WebsiteAuditLoopWorkflow"/> once per section. Asks the agent for
/// a structured report + issue count so the loop can aggregate totals without
/// re-parsing free-form text.
/// </summary>
/// <remarks>
/// Container-lifecycle branching mirrors <see cref="SimpleAgentWorkflow"/> (Fix #2).
/// <see cref="WebsiteAuditLoopWorkflow"/> dispatches with its own container id;
/// top-level HTTP dispatch of WebsiteAuditCore sends empty, so the workflow
/// spawns its own container (destroyed in <c>finally</c>).
/// </remarks>
[Workflow]
public class WebsiteAuditCoreWorkflow
{
    private const string StructuredSchema = """
        {
          "type": "object",
          "properties": {
            "report": { "type": "string" },
            "issueCount": { "type": "integer" }
          },
          "required": ["report", "issueCount"]
        }
        """;

    [WorkflowRun]
    public async Task<WebsiteAuditCoreOutput> RunAsync(WebsiteAuditCoreInput input)
    {
        string containerId;
        bool ownsContainer;
        if (!string.IsNullOrWhiteSpace(input.ContainerId))
        {
            containerId = input.ContainerId;
            ownsContainer = false;
        }
        else
        {
            var spawnInput = new SpawnContainerInput(
                SessionId: input.SessionId,
                WorkspacePath: input.WorkspacePath,
                EnableGui: false);

            var spawn = await Workflow.ExecuteActivityAsync(
                (DockerActivities a) => a.SpawnAsync(spawnInput),
                ActivityProfiles.Container);

            containerId = spawn.ContainerId;
            ownsContainer = true;
        }

        try
        {
            var prompt = $"""
                Audit the following website section for usability, accessibility, and performance issues.
                Section: {input.SectionId}
                Description: {input.SectionDescription}
                Return a structured audit report with an explicit issue count.
                """;

            var runInput = new RunCliAgentInput(
                Prompt: prompt,
                ContainerId: containerId,
                AiAssistant: input.AiAssistant,
                Model: input.Model,
                ModelPower: 2,
                WorkingDirectory: input.WorkspacePath,
                SessionId: input.SessionId,
                StructuredOutputSchema: StructuredSchema);

            var run = await Workflow.ExecuteActivityAsync(
                (AiActivities a) => a.RunCliAgentAsync(runInput),
                ActivityProfiles.Long);

            var (report, issueCount) = ParseStructured(run.StructuredOutputJson ?? run.Response);
            return new WebsiteAuditCoreOutput(
                SectionId: input.SectionId,
                AuditReport: report,
                IssueCount: issueCount,
                CostUsd: run.CostUsd);
        }
        finally
        {
            if (ownsContainer)
            {
                var destroyInput = new DestroyInput(containerId);
                await Workflow.ExecuteActivityAsync(
                    (DockerActivities a) => a.DestroyAsync(destroyInput),
                    ActivityProfiles.ContainerCleanup);
            }
        }
    }

    /// <summary>
    /// Pure parser — extracts report + issue count from the structured JSON. On
    /// any parse failure, falls back to returning the raw text as the report and
    /// zero issues. Deterministic: no side effects, safe under workflow replay.
    /// </summary>
    private static (string report, int issueCount) ParseStructured(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return ("", 0);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var report = root.TryGetProperty("report", out var r)
                ? r.GetString() ?? ""
                : "";
            var count = root.TryGetProperty("issueCount", out var c) && c.ValueKind == JsonValueKind.Number
                ? c.GetInt32()
                : 0;
            return (report, count);
        }
        catch
        {
            return (json, 0);
        }
    }
}
