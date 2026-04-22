// MagicPAI.Server/Workflows/Temporal/SimpleAgentWorkflow.cs
// Temporal port of the Elsa SimpleAgentWorkflow — full §8.4 shape with
// verification gates + requirements-coverage loop.
//
// Flow:
//   Spawn container → run agent → run verification gates →
//   for i = 1..MaxCoverageIterations:
//       grade coverage (AI) → if AllMet, stop
//       otherwise re-run agent with GapPrompt → re-run gates
//   Destroy container (in finally)
//
// Coexists with the Elsa SimpleAgentWorkflow in MagicPAI.Server.Workflows
// until Phase 3 removes the Elsa version. See temporal.md §8.4.
using Temporalio.Workflows;
using MagicPAI.Activities.AI;
using MagicPAI.Activities.Contracts;
using MagicPAI.Activities.Docker;
using MagicPAI.Activities.Verification;
using MagicPAI.Workflows;
using MagicPAI.Workflows.Contracts;

namespace MagicPAI.Server.Workflows;

/// <summary>
/// Temporal SimpleAgentWorkflow — §8.4 full form. Runs the AI CLI agent, runs
/// verification gates, and loops a coverage check up to
/// <see cref="SimpleAgentInput.MaxCoverageIterations"/> times. Container
/// lifetime is conditional: when dispatched top-level
/// (<see cref="SimpleAgentInput.ExistingContainerId"/> is null), the workflow
/// spawns a container on entry and destroys it in <c>finally</c>. When nested
/// under an orchestrator that already owns a container, the caller passes its
/// container id via <see cref="SimpleAgentInput.ExistingContainerId"/> and
/// this workflow reuses it without spawning or destroying.
/// </summary>
/// <remarks>
/// <para>
/// Expression-tree constraint: Temporal passes the
/// <see cref="Workflow.ExecuteActivityAsync{T,TResult}(Expression{Func{T,Task{TResult}}}, ActivityOptions)"/>
/// lambda as an <c>Expression&lt;&gt;</c>. Named arguments that are not in
/// positional order cannot appear inside the expression body (CS9307). Every
/// activity input record is therefore constructed in a local variable before
/// the lambda is built.
/// </para>
/// </remarks>
[Workflow]
public class SimpleAgentWorkflow
{
    private decimal _totalCost;
    private int _coverageIteration;

    [WorkflowQuery]
    public decimal TotalCostUsd => _totalCost;

    [WorkflowQuery]
    public int CoverageIteration => _coverageIteration;

    private static readonly IReadOnlyList<string> DefaultGates =
        new[] { "compile", "test", "hallucination" };

    [WorkflowRun]
    public async Task<SimpleAgentOutput> RunAsync(SimpleAgentInput input)
    {
        // Container-lifecycle branching: if the caller already owns a container
        // (orchestrator dispatched us as a child), reuse it and skip Spawn +
        // Destroy. Otherwise spawn here and destroy in finally. This avoids
        // double-spawn port collisions (noVNC 6080) when this workflow is
        // nested under FullOrchestrateWorkflow / OrchestrateSimplePathWorkflow.
        string containerId;
        bool ownsContainer;
        if (!string.IsNullOrWhiteSpace(input.ExistingContainerId))
        {
            containerId = input.ExistingContainerId;
            ownsContainer = false;
        }
        else
        {
            // Build spawn input outside the expression tree (CS9307 compliance).
            var spawnInput = new SpawnContainerInput(
                SessionId: input.SessionId,
                WorkspacePath: input.WorkspacePath,
                EnableGui: input.EnableGui);

            var spawn = await Workflow.ExecuteActivityAsync(
                (DockerActivities a) => a.SpawnAsync(spawnInput),
                ActivityProfiles.Container);

            containerId = spawn.ContainerId;
            ownsContainer = true;
        }

        try
        {
            // First pass: run the agent with the original prompt.
            var run = await RunAgentAsync(input, containerId, input.Prompt);
            _totalCost += run.CostUsd;
            var lastResponse = run.Response;
            var lastFilesModified = run.FilesModified;

            // Run verification gates against the first-pass output.
            var gates = input.EnabledGates ?? DefaultGates;
            var verifyInput = new VerifyInput(
                ContainerId: containerId,
                WorkingDirectory: input.WorkspacePath,
                EnabledGates: gates,
                WorkerOutput: run.Response,
                SessionId: input.SessionId);

            var verify = await Workflow.ExecuteActivityAsync(
                (VerifyActivities a) => a.RunGatesAsync(verifyInput),
                ActivityProfiles.Verify);

            // Fast-path: skip the coverage loop when gates passed on the first
            // try and the caller opted in. Avoids one GradeCoverage Claude call
            // (~5-10 s) per successful run. The coverage loop is still valuable
            // when the initial run compiles/tests cleanly but misses some
            // requirement — the default-off flag preserves that safety.
            var coverageEnabled = !(input.SkipCoverageWhenGatesPass && verify.AllPassed);

            // Requirements-coverage loop — up to MaxCoverageIterations iterations.
            for (_coverageIteration = 1;
                 coverageEnabled && _coverageIteration <= input.MaxCoverageIterations;
                 _coverageIteration++)
            {
                var coverageInput = new CoverageInput(
                    OriginalPrompt: input.Prompt,
                    ContainerId: containerId,
                    WorkingDirectory: input.WorkspacePath,
                    MaxIterations: input.MaxCoverageIterations,
                    CurrentIteration: _coverageIteration,
                    ModelPower: 2,
                    AiAssistant: input.AiAssistant,
                    SessionId: input.SessionId);

                var coverage = await Workflow.ExecuteActivityAsync(
                    (AiActivities a) => a.GradeCoverageAsync(coverageInput),
                    ActivityProfiles.Medium);

                if (coverage.AllMet)
                    break;

                // Re-run the agent with the gap-filling prompt.
                var repair = await RunAgentAsync(input, containerId, coverage.GapPrompt);
                _totalCost += repair.CostUsd;
                lastResponse = repair.Response;
                lastFilesModified = repair.FilesModified;

                // Re-verify after the repair pass.
                var reVerifyInput = new VerifyInput(
                    ContainerId: containerId,
                    WorkingDirectory: input.WorkspacePath,
                    EnabledGates: gates,
                    WorkerOutput: repair.Response,
                    SessionId: input.SessionId);

                verify = await Workflow.ExecuteActivityAsync(
                    (VerifyActivities a) => a.RunGatesAsync(reVerifyInput),
                    ActivityProfiles.Verify);
            }

            return new SimpleAgentOutput(
                Response: lastResponse,
                VerificationPassed: verify.AllPassed,
                CoverageIterations: _coverageIteration,
                TotalCostUsd: _totalCost,
                FilesModified: lastFilesModified);
        }
        finally
        {
            // Only destroy containers we spawned ourselves. If the caller
            // provided an ExistingContainerId, destroying it would tear down
            // the parent's container while the parent is still using it.
            if (ownsContainer)
            {
                var destroyInput = new DestroyInput(containerId);
                await Workflow.ExecuteActivityAsync(
                    (DockerActivities a) => a.DestroyAsync(destroyInput),
                    ActivityProfiles.ContainerCleanup);
            }
        }
    }

    private Task<RunCliAgentOutput> RunAgentAsync(
        SimpleAgentInput input, string containerId, string prompt)
    {
        var runInput = new RunCliAgentInput(
            Prompt: prompt,
            ContainerId: containerId,
            AiAssistant: input.AiAssistant,
            Model: input.Model,
            ModelPower: input.ModelPower,
            WorkingDirectory: input.WorkspacePath,
            SessionId: input.SessionId);

        return Workflow.ExecuteActivityAsync(
            (AiActivities a) => a.RunCliAgentAsync(runInput),
            ActivityProfiles.Long);
    }
}
