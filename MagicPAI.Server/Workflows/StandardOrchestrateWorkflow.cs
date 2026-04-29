// MagicPAI.Server/Workflows/Temporal/StandardOrchestrateWorkflow.cs
// Temporal port of the Elsa StandardOrchestrateWorkflow. Middle-complexity
// orchestrator sitting between SimpleAgentWorkflow and FullOrchestrateWorkflow:
// spawns a container, enhances the prompt, runs the agent, then delegates
// verify-and-repair to the child workflow. See temporal.md §H.9.
using Temporalio.Workflows;
using MagicPAI.Activities.AI;
using MagicPAI.Activities.Contracts;
using MagicPAI.Activities.Docker;
using MagicPAI.Activities.Stage;
using MagicPAI.Workflows;
using MagicPAI.Workflows.Contracts;

namespace MagicPAI.Server.Workflows;

/// <summary>
/// Standard orchestration path. Owns container lifecycle (spawn + destroy in
/// finally) and composes EnhancePrompt → RunCliAgent → VerifyAndRepair (child).
/// </summary>
[Workflow]
public class StandardOrchestrateWorkflow
{
    private decimal _totalCost;

    [WorkflowQuery]
    public decimal TotalCostUsd => _totalCost;

    [WorkflowRun]
    public async Task<StandardOrchestrateOutput> RunAsync(StandardOrchestrateInput input)
    {
        await EmitStageAsync(input.SessionId, "spawning-container");

        var spawnInput = new SpawnContainerInput(
            SessionId: input.SessionId,
            WorkspacePath: input.WorkspacePath,
            EnableGui: input.EnableGui);

        var spawn = await Workflow.ExecuteActivityAsync(
            (DockerActivities a) => a.SpawnAsync(spawnInput),
            ActivityProfiles.Container);

        try
        {
            // Step 1 — enhance the original prompt for specificity.
            await EmitStageAsync(input.SessionId, "enhancing-prompt");

            var enhanceInput = new EnhancePromptInput(
                OriginalPrompt: input.Prompt,
                EnhancementInstructions: "Improve specificity and add missing context.",
                ContainerId: spawn.ContainerId,
                ModelPower: 2,
                AiAssistant: input.AiAssistant,
                SessionId: input.SessionId);

            var enhance = await Workflow.ExecuteActivityAsync(
                (AiActivities a) => a.EnhancePromptAsync(enhanceInput),
                ActivityProfiles.Medium);

            // Step 2 — run the enhanced prompt through the agent.
            await EmitStageAsync(input.SessionId, "running-agent");

            var runInput = new RunCliAgentInput(
                Prompt: enhance.EnhancedPrompt,
                ContainerId: spawn.ContainerId,
                AiAssistant: input.AiAssistant,
                Model: input.Model,
                ModelPower: input.ModelPower,
                WorkingDirectory: input.WorkspacePath,
                SessionId: input.SessionId);

            var run = await Workflow.ExecuteActivityAsync(
                (AiActivities a) => a.RunCliAgentAsync(runInput),
                ActivityProfiles.Long);

            _totalCost += run.CostUsd;
            await EmitCostAsync(input.SessionId, _totalCost);

            // Step 3 — verify + repair via the reusable child workflow.
            await EmitStageAsync(input.SessionId, "verify-and-repair");

            var verifyInput = new VerifyAndRepairInput(
                SessionId: input.SessionId,
                ContainerId: spawn.ContainerId,
                WorkingDirectory: input.WorkspacePath,
                OriginalPrompt: input.Prompt,
                AiAssistant: input.AiAssistant,
                Model: input.Model,
                ModelPower: input.ModelPower,
                Gates: new[] { "compile", "test" },
                WorkerOutput: run.Response);

            var verify = await Workflow.ExecuteChildWorkflowAsync(
                (VerifyAndRepairWorkflow w) => w.RunAsync(verifyInput),
                new ChildWorkflowOptions { Id = $"{input.SessionId}-verify" });

            _totalCost += verify.RepairCostUsd;
            await EmitCostAsync(input.SessionId, _totalCost);
            await EmitStageAsync(input.SessionId, "done");

            return new StandardOrchestrateOutput(
                Response: run.Response,
                VerificationPassed: verify.Success,
                TotalCostUsd: _totalCost);
        }
        finally
        {
            var destroyInput = new DestroyInput(spawn.ContainerId);
            await Workflow.ExecuteActivityAsync(
                (DockerActivities a) => a.DestroyAsync(destroyInput),
                ActivityProfiles.ContainerCleanup);
        }
    }

    /// <summary>
    /// Emit a stage transition. Gated on <c>Workflow.Patched("emit-stage-activity-v1")</c>
    /// so old workflow histories — which never scheduled this activity — replay
    /// deterministically.
    /// </summary>
    private static async Task EmitStageAsync(string sessionId, string stage)
    {
        if (!Workflow.Patched("emit-stage-activity-v1")) return;

        var stageInput = new EmitStageInput(sessionId, stage);
        await Workflow.ExecuteActivityAsync(
            (StageActivities a) => a.EmitStageAsync(stageInput),
            ActivityProfiles.Short);
    }

    /// <summary>
    /// Broadcast running cost. Gated on <c>Workflow.Patched("emit-cost-activity-v1")</c>
    /// for replay safety.
    /// </summary>
    private static async Task EmitCostAsync(string sessionId, decimal totalCost)
    {
        if (!Workflow.Patched("emit-cost-activity-v1")) return;

        var costInput = new EmitCostInput(sessionId, totalCost);
        await Workflow.ExecuteActivityAsync(
            (StageActivities a) => a.EmitCostAsync(costInput),
            ActivityProfiles.Short);
    }
}
