// MagicPAI.Server/Workflows/Temporal/ResearchPipelineWorkflow.cs
// Temporal port of the Elsa ResearchPipelineWorkflow. Thin wrapper around the
// ResearchPromptAsync activity using the strongest model (ModelPower=1) so that
// research quality is maximized; this differs from the default ModelPower=2
// used by FullOrchestrate's inline research. See temporal.md §H.8.
using Temporalio.Workflows;
using MagicPAI.Activities.AI;
using MagicPAI.Activities.Contracts;
using MagicPAI.Activities.Docker;
using MagicPAI.Workflows;
using MagicPAI.Workflows.Contracts;

namespace MagicPAI.Server.Workflows;

/// <summary>
/// Runs <see cref="AiActivities.ResearchPromptAsync"/> against an existing
/// container and returns the researched prompt + context. Uses ModelPower=1
/// (strongest model) since deep research is the whole point of this workflow.
/// </summary>
/// <remarks>
/// Container-lifecycle branching mirrors <see cref="SimpleAgentWorkflow"/> (Fix #2).
/// When dispatched top-level via HTTP, <c>ContainerId</c> is empty and the
/// workflow spawns its own container (destroyed in <c>finally</c>). When nested,
/// it reuses the caller's container.
/// </remarks>
[Workflow]
public class ResearchPipelineWorkflow
{
    [WorkflowRun]
    public async Task<ResearchPipelineOutput> RunAsync(ResearchPipelineInput input)
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
            var researchInput = new ResearchPromptInput(
                Prompt: input.Prompt,
                AiAssistant: input.AiAssistant,
                ContainerId: containerId,
                ModelPower: 1,
                SessionId: input.SessionId);

            var research = await Workflow.ExecuteActivityAsync(
                (AiActivities a) => a.ResearchPromptAsync(researchInput),
                ActivityProfiles.Long);

            return new ResearchPipelineOutput(
                ResearchedPrompt: research.EnhancedPrompt,
                ResearchContext: research.ResearchContext,
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
