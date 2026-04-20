// MagicPAI.Server/Workflows/Temporal/PromptEnhancerWorkflow.cs
// Temporal port of the Elsa PromptEnhancerWorkflow. Thin wrapper around
// AiActivities.EnhancePromptAsync so clients can dispatch prompt enhancement as a
// standalone workflow (surfacing progress + cost through Temporal history).
// See temporal.md §H.2.
using Temporalio.Workflows;
using MagicPAI.Activities.AI;
using MagicPAI.Activities.Contracts;
using MagicPAI.Activities.Docker;
using MagicPAI.Workflows;
using MagicPAI.Workflows.Contracts;

namespace MagicPAI.Server.Workflows;

/// <summary>
/// Runs the prompt-enhancement activity once. Callers pass optional enhancement
/// instructions; when omitted, the workflow uses a sensible default that asks the
/// model to improve clarity without changing intent.
/// </summary>
/// <remarks>
/// Container-lifecycle branching mirrors <see cref="SimpleAgentWorkflow"/> (Fix #2).
/// When dispatched top-level via HTTP, <c>ContainerId</c> is empty and the workflow
/// spawns its own container (destroyed in <c>finally</c>). When nested, the caller
/// supplies its <c>ContainerId</c> and this workflow reuses it.
/// </remarks>
[Workflow]
public class PromptEnhancerWorkflow
{
    [WorkflowRun]
    public async Task<PromptEnhancerOutput> RunAsync(PromptEnhancerInput input)
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
            var enhanceInput = new EnhancePromptInput(
                OriginalPrompt: input.OriginalPrompt,
                EnhancementInstructions:
                    input.EnhancementInstructions
                        ?? "Improve clarity, add missing context, preserve intent.",
                ContainerId: containerId,
                ModelPower: input.ModelPower,
                AiAssistant: input.AiAssistant,
                SessionId: input.SessionId);

            var result = await Workflow.ExecuteActivityAsync(
                (AiActivities a) => a.EnhancePromptAsync(enhanceInput),
                ActivityProfiles.Medium);

            // Cost is emitted by the activity through ISessionStreamSink; the workflow
            // returns 0 here so the output record stays decoupled from the activity's
            // internal token accounting. See temporal.md §H.2.
            return new PromptEnhancerOutput(
                EnhancedPrompt: result.EnhancedPrompt,
                WasEnhanced: result.WasEnhanced,
                Rationale: result.Rationale,
                CostUsd: 0m);
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
