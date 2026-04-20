// MagicPAI.Server/Workflows/Temporal/ClawEvalAgentWorkflow.cs
// Temporal port of the Elsa ClawEvalAgentWorkflow. Specialized for evaluation
// runs — runs the agent then verifies with compile/test/coverage gates. Expects
// the caller (evaluation harness) to own container lifecycle. See temporal.md §H.10.
using Temporalio.Workflows;
using MagicPAI.Activities.AI;
using MagicPAI.Activities.Contracts;
using MagicPAI.Activities.Docker;
using MagicPAI.Activities.Verification;
using MagicPAI.Workflows;
using MagicPAI.Workflows.Contracts;

namespace MagicPAI.Server.Workflows;

/// <summary>
/// Runs a single evaluation: agent invocation followed by the full eval gate
/// set (compile, test, coverage). Surfaces the raw structured gate results JSON
/// in <see cref="ClawEvalAgentOutput.EvalReport"/> so the evaluation harness
/// can assert per-gate expectations.
/// </summary>
/// <remarks>
/// Container-lifecycle branching mirrors <see cref="SimpleAgentWorkflow"/> (Fix #2).
/// Evaluation harnesses typically own container lifecycle and pass a non-empty
/// <c>ContainerId</c>; top-level HTTP dispatch supplies empty and the workflow
/// spawns its own container (destroyed in <c>finally</c>).
/// </remarks>
[Workflow]
public class ClawEvalAgentWorkflow
{
    [WorkflowRun]
    public async Task<ClawEvalAgentOutput> RunAsync(ClawEvalAgentInput input)
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
            // Step 1 — run the agent against the eval prompt.
            var runInput = new RunCliAgentInput(
                Prompt: input.Prompt,
                ContainerId: containerId,
                AiAssistant: input.AiAssistant,
                Model: input.Model,
                ModelPower: input.ModelPower,
                WorkingDirectory: input.WorkspacePath,
                SessionId: input.SessionId);

            var run = await Workflow.ExecuteActivityAsync(
                (AiActivities a) => a.RunCliAgentAsync(runInput),
                ActivityProfiles.Long);

            // Step 2 — run the full eval gate set (compile + test + coverage).
            var verifyInput = new VerifyInput(
                ContainerId: containerId,
                WorkingDirectory: input.WorkspacePath,
                EnabledGates: new[] { "compile", "test", "coverage" },
                WorkerOutput: run.Response,
                SessionId: input.SessionId);

            var verify = await Workflow.ExecuteActivityAsync(
                (VerifyActivities a) => a.RunGatesAsync(verifyInput),
                ActivityProfiles.Verify);

            return new ClawEvalAgentOutput(
                Response: run.Response,
                PassedEval: verify.AllPassed,
                EvalReport: verify.GateResultsJson,
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
}
