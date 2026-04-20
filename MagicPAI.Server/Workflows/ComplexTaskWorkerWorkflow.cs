// MagicPAI.Server/Workflows/Temporal/ComplexTaskWorkerWorkflow.cs
// Temporal port of the Elsa ComplexTaskWorkerWorkflow. Child workflow launched
// by OrchestrateComplexPath for each decomposed task: claims the files touched,
// runs the agent on the task description, releases the claims in a finally
// block. See temporal.md §H.6.
using Temporalio.Workflows;
using MagicPAI.Activities.AI;
using MagicPAI.Activities.Contracts;
using MagicPAI.Activities.Infrastructure;
using MagicPAI.Activities.Verification;
using MagicPAI.Workflows;
using MagicPAI.Workflows.Contracts;

namespace MagicPAI.Server.Workflows;

/// <summary>
/// Runs one decomposed task from the OrchestrateComplexPath plan. Claims every
/// file in <see cref="ComplexTaskInput.FilesTouched"/> via
/// <c>BlackboardActivities.ClaimFileAsync</c>. On conflict, waits 30 s and
/// retries once. If the retry still loses, returns a non-success output without
/// running the agent. Otherwise runs the agent, verifies the result with a
/// compile+test gate set, optionally runs ONE repair iteration on gate failure
/// (re-running the agent with a gate-driven repair prompt), and releases all
/// claimed files in a <c>finally</c> block.
/// </summary>
/// <remarks>
/// Workflow time (<see cref="Workflow.DelayAsync(TimeSpan)"/>) is used instead
/// of <c>Task.Delay</c> to keep the workflow deterministic and so time-skipping
/// tests can fast-forward the retry wait.
/// </remarks>
[Workflow]
public class ComplexTaskWorkerWorkflow
{
    // Per-subtask verify-and-repair uses a compile+test gate set only.
    // Hallucination is deliberately excluded — an individual subtask may
    // intentionally leave intermediate / not-yet-wired state that the parent
    // orchestrator's coverage loop will close. Bigger-picture correctness is
    // the parent's job.
    private static readonly IReadOnlyList<string> SubtaskGates =
        new[] { "compile", "test" };

    [WorkflowRun]
    public async Task<ComplexTaskOutput> RunAsync(ComplexTaskInput input)
    {
        // Claim each file up front — if any file is held by a sibling, wait
        // 30 s and retry once. On the second failure, return without running
        // the agent. Claim-failures before the try/finally mean we cannot own
        // any partial claim at release time, so we simply release the listed
        // FilesTouched in the finally; ReleaseFile is ownership-checked so
        // releasing an unowned file is a no-op.
        foreach (var file in input.FilesTouched)
        {
            var claimInput = new ClaimFileInput(
                FilePath: file,
                TaskId: input.TaskId,
                SessionId: input.ParentSessionId);

            var claim = await Workflow.ExecuteActivityAsync(
                (BlackboardActivities a) => a.ClaimFileAsync(claimInput),
                ActivityProfiles.Short);

            if (!claim.Claimed)
            {
                // File is owned by another sibling task; wait 30 s and try once more.
                await Workflow.DelayAsync(TimeSpan.FromSeconds(30));

                claim = await Workflow.ExecuteActivityAsync(
                    (BlackboardActivities a) => a.ClaimFileAsync(claimInput),
                    ActivityProfiles.Short);

                if (!claim.Claimed)
                {
                    // Release any previously-claimed files so siblings aren't blocked.
                    await ReleaseAllAsync(input);
                    return new ComplexTaskOutput(
                        TaskId: input.TaskId,
                        Success: false,
                        Response: $"File {file} claimed by {claim.CurrentOwner}",
                        CostUsd: 0m,
                        FilesModified: Array.Empty<string>());
                }
            }
        }

        try
        {
            var runInput = new RunCliAgentInput(
                Prompt: input.Description,
                ContainerId: input.ContainerId,
                AiAssistant: input.AiAssistant,
                Model: input.Model,
                ModelPower: input.ModelPower,
                WorkingDirectory: input.WorkspacePath,
                SessionId: input.ParentSessionId);

            var run = await Workflow.ExecuteActivityAsync(
                (AiActivities a) => a.RunCliAgentAsync(runInput),
                ActivityProfiles.Long);

            var finalResponse = run.Response;
            var finalFilesModified = run.FilesModified;
            var finalCost = run.CostUsd;
            var finalSuccess = run.Success;

            // Post-agent verify → at most ONE repair iteration → re-verify.
            // Intentionally capped at 1 attempt inside a single subtask; the
            // parent orchestrator's coverage loop handles the bigger picture.
            var verifyInput = new VerifyInput(
                ContainerId: input.ContainerId,
                WorkingDirectory: input.WorkspacePath,
                EnabledGates: SubtaskGates,
                WorkerOutput: run.Response,
                SessionId: input.ParentSessionId);

            var verify = await Workflow.ExecuteActivityAsync(
                (VerifyActivities a) => a.RunGatesAsync(verifyInput),
                ActivityProfiles.Verify);

            if (!verify.AllPassed)
            {
                // One repair attempt. Build a repair prompt from the failed
                // gates + gate results and re-run the agent on the same
                // container / workspace. Then re-verify.
                var repairPromptInput = new RepairInput(
                    ContainerId: input.ContainerId,
                    FailedGates: verify.FailedGates,
                    OriginalPrompt: input.Description,
                    GateResultsJson: verify.GateResultsJson,
                    AttemptNumber: 1,
                    MaxAttempts: 1);

                var repairPrompt = await Workflow.ExecuteActivityAsync(
                    (VerifyActivities a) => a.GenerateRepairPromptAsync(repairPromptInput),
                    ActivityProfiles.Short);

                if (repairPrompt.ShouldAttemptRepair)
                {
                    var repairRunInput = new RunCliAgentInput(
                        Prompt: repairPrompt.RepairPrompt,
                        ContainerId: input.ContainerId,
                        AiAssistant: input.AiAssistant,
                        Model: input.Model,
                        ModelPower: input.ModelPower,
                        WorkingDirectory: input.WorkspacePath,
                        SessionId: input.ParentSessionId);

                    var repairRun = await Workflow.ExecuteActivityAsync(
                        (AiActivities a) => a.RunCliAgentAsync(repairRunInput),
                        ActivityProfiles.Long);

                    finalResponse = repairRun.Response;
                    finalFilesModified = repairRun.FilesModified;
                    finalCost += repairRun.CostUsd;
                    finalSuccess = repairRun.Success;

                    var reVerifyInput = new VerifyInput(
                        ContainerId: input.ContainerId,
                        WorkingDirectory: input.WorkspacePath,
                        EnabledGates: SubtaskGates,
                        WorkerOutput: repairRun.Response,
                        SessionId: input.ParentSessionId);

                    verify = await Workflow.ExecuteActivityAsync(
                        (VerifyActivities a) => a.RunGatesAsync(reVerifyInput),
                        ActivityProfiles.Verify);
                }
            }

            return new ComplexTaskOutput(
                TaskId: input.TaskId,
                Success: finalSuccess,
                Response: finalResponse,
                CostUsd: finalCost,
                FilesModified: finalFilesModified,
                VerificationPassed: verify.AllPassed);
        }
        finally
        {
            await ReleaseAllAsync(input);
        }
    }

    private static async Task ReleaseAllAsync(ComplexTaskInput input)
    {
        foreach (var file in input.FilesTouched)
        {
            var releaseInput = new ReleaseFileInput(
                FilePath: file,
                TaskId: input.TaskId,
                SessionId: input.ParentSessionId);

            await Workflow.ExecuteActivityAsync(
                (BlackboardActivities a) => a.ReleaseFileAsync(releaseInput),
                ActivityProfiles.Short);
        }
    }
}
