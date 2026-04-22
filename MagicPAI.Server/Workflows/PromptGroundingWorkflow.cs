// MagicPAI.Server/Workflows/Temporal/PromptGroundingWorkflow.cs
// Temporal port of the Elsa PromptGroundingWorkflow. First child-workflow-invoking
// Temporal workflow in the codebase — invokes ContextGathererWorkflow and then
// EnhancePromptAsync so the resulting prompt is grounded in the gathered context.
// See temporal.md §H.4.
using Temporalio.Workflows;
using MagicPAI.Activities.AI;
using MagicPAI.Activities.Contracts;
using MagicPAI.Activities.Docker;
using MagicPAI.Workflows;
using MagicPAI.Workflows.Contracts;

namespace MagicPAI.Server.Workflows;

/// <summary>
/// Two-step prompt grounding: (1) invoke <see cref="ContextGathererWorkflow"/> to
/// produce a context blob, (2) call the enhance-prompt activity with a rewrite
/// instruction that references that context.
/// </summary>
/// <remarks>
/// Container-lifecycle branching mirrors <see cref="SimpleAgentWorkflow"/> (Fix #2).
/// Top-level HTTP dispatch spawns its own container; nested invocation reuses the
/// caller's. The spawned container is shared with the child
/// <see cref="ContextGathererWorkflow"/> so both steps run against the same
/// workspace.
/// </remarks>
[Workflow]
public class PromptGroundingWorkflow
{
    [WorkflowRun]
    public async Task<PromptGroundingOutput> RunAsync(PromptGroundingInput input)
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
            // Step 1 — invoke the context-gatherer child workflow.
            var contextInput = new ContextGathererInput(
                SessionId: input.SessionId,
                Prompt: input.Prompt,
                ContainerId: containerId,
                WorkingDirectory: input.WorkingDirectory,
                AiAssistant: input.AiAssistant);

            var contextOptions = new ChildWorkflowOptions { Id = $"{input.SessionId}-context" };

            var context = await Workflow.ExecuteChildWorkflowAsync(
                (ContextGathererWorkflow w) => w.RunAsync(contextInput),
                contextOptions);

            // Step 2 — enhance the prompt so it references the gathered context.
            // Instruction string built outside the Expression lambda because the
            // Expression tree can't hold named-argument specifications out of
            // position (CS9307), see Day 3 agent notes in SimpleAgentWorkflow.cs.
            var enhanceInput = new EnhancePromptInput(
                OriginalPrompt: input.Prompt,
                EnhancementInstructions:
                    $"Rewrite to reference this codebase context:\n{context.GatheredContext}",
                ContainerId: containerId,
                ModelPower: 2,
                AiAssistant: input.AiAssistant,
                SessionId: input.SessionId);

            var enhance = await Workflow.ExecuteActivityAsync(
                (AiActivities a) => a.EnhancePromptAsync(enhanceInput),
                ActivityProfiles.Medium);

            return new PromptGroundingOutput(
                GroundedPrompt: enhance.EnhancedPrompt,
                Rationale: enhance.Rationale ?? "",
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
