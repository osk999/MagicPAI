// MagicPAI.Server/Workflows/Temporal/ContextGathererWorkflow.cs
// Temporal port of the Elsa ContextGathererWorkflow. Fans out three context
// sources in parallel (codebase research, repo map, memory recall) and
// concatenates them into a single "gathered context" blob with section headers
// for downstream workflows. See temporal.md §H.3.
using Temporalio.Workflows;
using MagicPAI.Activities.AI;
using MagicPAI.Activities.Contracts;
using MagicPAI.Activities.Docker;
using MagicPAI.Workflows;
using MagicPAI.Workflows.Contracts;

namespace MagicPAI.Server.Workflows;

/// <summary>
/// Three-way parallel context gatherer. Runs codebase research (the original
/// behaviour), a lightweight repo-map pass, and a memory-recall pass against
/// the same container, then stitches them into a single markdown blob with
/// H1 section headers. The default model-power for the heavy research call is
/// 2 (balanced); the two cheap passes use power=3. Callers wanting deep
/// research should use <see cref="ResearchPipelineWorkflow"/> instead, which
/// uses power=1 (strongest).
/// </summary>
/// <remarks>
/// Container-lifecycle branching mirrors <see cref="SimpleAgentWorkflow"/> (Fix #2).
/// When dispatched top-level (empty ContainerId), the workflow spawns a single
/// container used by all three parallel probes and destroys it in <c>finally</c>.
/// When invoked nested (e.g. from <see cref="PromptGroundingWorkflow"/>), it
/// reuses the caller's container.
/// </remarks>
[Workflow]
public class ContextGathererWorkflow
{
    [WorkflowRun]
    public async Task<ContextGathererOutput> RunAsync(ContextGathererInput input)
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
                WorkspacePath: input.WorkingDirectory,
                EnableGui: false);

            var spawn = await Workflow.ExecuteActivityAsync(
                (DockerActivities a) => a.SpawnAsync(spawnInput),
                ActivityProfiles.Container);

            containerId = spawn.ContainerId;
            ownsContainer = true;
        }

        try
        {
            // ── 1. Codebase research ──────────────────────────────────────────
            var researchInput = new ResearchPromptInput(
                Prompt: input.Prompt,
                AiAssistant: input.AiAssistant,
                ContainerId: containerId,
                ModelPower: 2,
                SessionId: input.SessionId);

            // CS9307: build inputs in locals before each ExecuteActivityAsync lambda
            // so the expression-tree compiler sees a simple field read inside the
            // lambda body, not a record-construction expression.
            var researchTask = Workflow.ExecuteActivityAsync(
                (AiActivities a) => a.ResearchPromptAsync(researchInput),
                ActivityProfiles.Long);

            // ── 2. Repo map (cheap, structure-only) ───────────────────────────
            var repoMapInput = new RunCliAgentInput(
                Prompt: "List every top-level directory and the one most important file in it, as bullet points. No code, ≤40 lines.",
                ContainerId: containerId,
                AiAssistant: input.AiAssistant,
                Model: null,
                ModelPower: 3,                     // cheapest model
                WorkingDirectory: input.WorkingDirectory,
                MaxTurns: 3,
                SessionId: input.SessionId);

            var repoMapTask = Workflow.ExecuteActivityAsync(
                (AiActivities a) => a.RunCliAgentAsync(repoMapInput),
                ActivityProfiles.Medium);

            // ── 3. Memory recall (CLAUDE.md + top-level docs) ─────────────────
            var memoryInput = new RunCliAgentInput(
                Prompt: $"Scan CLAUDE.md and any top-level docs for conventions relevant to: {input.Prompt}. ≤30 lines. If nothing relevant, say so.",
                ContainerId: containerId,
                AiAssistant: input.AiAssistant,
                Model: null,
                ModelPower: 3,                     // cheapest model
                WorkingDirectory: input.WorkingDirectory,
                MaxTurns: 3,
                SessionId: input.SessionId);

            var memoryTask = Workflow.ExecuteActivityAsync(
                (AiActivities a) => a.RunCliAgentAsync(memoryInput),
                ActivityProfiles.Medium);

            // Fan-in. All three must complete (or one fail) before we proceed.
            await Workflow.WhenAllAsync(researchTask, repoMapTask, memoryTask);

            var research = await researchTask;
            var repoMap = await repoMapTask;
            var memory = await memoryTask;

            // Stitch with H1 section headers so downstream prompts can reason
            // about which slice came from which probe.
            var gathered =
                $"# Codebase Analysis\n{research.CodebaseAnalysis}\n\n" +
                $"# Research Context\n{research.ResearchContext}\n\n" +
                $"# Repository Map\n{repoMap.Response}\n\n" +
                $"# Relevant Memory\n{memory.Response}";

            // ResearchPromptOutput has no cost field — only the two CLI runs report cost.
            var totalCost = repoMap.CostUsd + memory.CostUsd;

            return new ContextGathererOutput(
                GatheredContext: gathered,
                ReferencedFiles: Array.Empty<string>(),
                CostUsd: totalCost);
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
}
