// MagicPAI.Server/Workflows/Temporal/VerifyAndRepairWorkflow.cs
// Temporal port of the Elsa VerifyAndRepairWorkflow. Reusable child workflow —
// parent orchestrators (StandardOrchestrate, FullOrchestrate, …) invoke it via
// Workflow.ExecuteChildWorkflowAsync. See temporal.md §H.1.
using Temporalio.Workflows;
using MagicPAI.Activities.AI;
using MagicPAI.Activities.Contracts;
using MagicPAI.Activities.Docker;
using MagicPAI.Activities.Stage;
using MagicPAI.Activities.Verification;
using MagicPAI.Workflows;
using MagicPAI.Workflows.Contracts;

namespace MagicPAI.Server.Workflows;

/// <summary>
/// Runs verification gates and — on failure — loops up to <see
/// cref="VerifyAndRepairInput.MaxRepairAttempts"/> times, each iteration generating a
/// repair prompt and re-running the agent. Exposes a <c>RepairAttempts</c> query so
/// Studio can surface progress while the loop is running.
/// </summary>
/// <remarks>
/// Container-lifecycle branching mirrors <see cref="SimpleAgentWorkflow"/> (Fix #2).
/// Parent orchestrators pass a non-empty <c>ContainerId</c>; top-level HTTP
/// dispatch sends empty and the workflow spawns its own container (destroyed in
/// <c>finally</c>).
/// </remarks>
[Workflow]
public class VerifyAndRepairWorkflow
{
    private int _repairAttempts;
    private decimal _repairCostUsd;

    [WorkflowQuery]
    public int RepairAttempts => _repairAttempts;

    /// <summary>
    /// Running cost of repair iterations. Mirrors <see cref="RepairAttempts"/>
    /// for the cost dimension so Studio's cost tile can reflect this child
    /// workflow's spend live (Phase-1 gap: query was missing).
    /// </summary>
    [WorkflowQuery]
    public decimal TotalCostUsd => _repairCostUsd;

    [WorkflowRun]
    public async Task<VerifyAndRepairOutput> RunAsync(VerifyAndRepairInput input)
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
            var currentOutput = input.WorkerOutput;

            while (true)
            {
                await EmitStageAsync(input.SessionId, "gates-running");

                // Step 1 — run the gates against the current output.
                var verifyInput = new VerifyInput(
                    ContainerId: containerId,
                    WorkingDirectory: input.WorkingDirectory,
                    EnabledGates: input.Gates,
                    WorkerOutput: currentOutput,
                    SessionId: input.SessionId);

                var verify = await Workflow.ExecuteActivityAsync(
                    (VerifyActivities a) => a.RunGatesAsync(verifyInput),
                    ActivityProfiles.Verify);

                if (verify.AllPassed)
                {
                    await EmitStageAsync(input.SessionId, "gates-passing");
                    await EmitStageAsync(input.SessionId, "done");
                    return new VerifyAndRepairOutput(
                        Success: true,
                        RepairAttempts: _repairAttempts,
                        FinalFailedGates: Array.Empty<string>(),
                        RepairCostUsd: _repairCostUsd);
                }

                if (_repairAttempts >= input.MaxRepairAttempts)
                {
                    await EmitStageAsync(input.SessionId, "done");
                    return new VerifyAndRepairOutput(
                        Success: false,
                        RepairAttempts: _repairAttempts,
                        FinalFailedGates: verify.FailedGates,
                        RepairCostUsd: _repairCostUsd);
                }

                _repairAttempts++;
                await EmitStageAsync(input.SessionId, "repairing");

                // Step 2 — generate a repair prompt from the failed gates.
                var repairInput = new RepairInput(
                    ContainerId: containerId,
                    FailedGates: verify.FailedGates,
                    OriginalPrompt: input.OriginalPrompt,
                    GateResultsJson: verify.GateResultsJson,
                    AttemptNumber: _repairAttempts,
                    MaxAttempts: input.MaxRepairAttempts);

                var repairPrompt = await Workflow.ExecuteActivityAsync(
                    (VerifyActivities a) => a.GenerateRepairPromptAsync(repairInput),
                    ActivityProfiles.Short);

                if (!repairPrompt.ShouldAttemptRepair)
                {
                    await EmitStageAsync(input.SessionId, "done");
                    return new VerifyAndRepairOutput(
                        Success: false,
                        RepairAttempts: _repairAttempts,
                        FinalFailedGates: verify.FailedGates,
                        RepairCostUsd: _repairCostUsd);
                }

                // Step 3 — rerun the agent with the repair prompt.
                var rerunInput = new RunCliAgentInput(
                    Prompt: repairPrompt.RepairPrompt,
                    ContainerId: containerId,
                    AiAssistant: input.AiAssistant,
                    Model: input.Model,
                    ModelPower: input.ModelPower,
                    WorkingDirectory: input.WorkingDirectory,
                    SessionId: input.SessionId);

                var rerun = await Workflow.ExecuteActivityAsync(
                    (AiActivities a) => a.RunCliAgentAsync(rerunInput),
                    ActivityProfiles.Long);

                _repairCostUsd += rerun.CostUsd;
                await EmitCostAsync(input.SessionId, _repairCostUsd);
                currentOutput = rerun.Response;
            }
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
