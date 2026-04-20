// MagicPAI.Server/Workflows/Temporal/VerifyAndRepairWorkflow.cs
// Temporal port of the Elsa VerifyAndRepairWorkflow. Reusable child workflow —
// parent orchestrators (StandardOrchestrate, FullOrchestrate, …) invoke it via
// Workflow.ExecuteChildWorkflowAsync. See temporal.md §H.1.
using Temporalio.Workflows;
using MagicPAI.Activities.AI;
using MagicPAI.Activities.Contracts;
using MagicPAI.Activities.Docker;
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
                    return new VerifyAndRepairOutput(
                        Success: true,
                        RepairAttempts: _repairAttempts,
                        FinalFailedGates: Array.Empty<string>(),
                        RepairCostUsd: _repairCostUsd);
                }

                if (_repairAttempts >= input.MaxRepairAttempts)
                {
                    return new VerifyAndRepairOutput(
                        Success: false,
                        RepairAttempts: _repairAttempts,
                        FinalFailedGates: verify.FailedGates,
                        RepairCostUsd: _repairCostUsd);
                }

                _repairAttempts++;

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
}
