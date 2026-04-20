// MagicPAI.Server/Workflows/Temporal/OrchestrateComplexPathWorkflow.cs
// Temporal port of the Elsa OrchestrateComplexPathWorkflow. Decomposes a prompt
// via AiActivities.ArchitectAsync, then fans out one ComplexTaskWorkerWorkflow
// child per task. Awaits completions with cancellation support via the
// CancelAllTasks signal. See temporal.md §8.5.
using Temporalio.Exceptions;
using Temporalio.Workflows;
using MagicPAI.Activities.AI;
using MagicPAI.Activities.Contracts;
using MagicPAI.Activities.Docker;
using MagicPAI.Workflows;
using MagicPAI.Workflows.Contracts;

namespace MagicPAI.Server.Workflows;

/// <summary>
/// Orchestrates the "complex" execution path. Calls the Architect activity to
/// decompose the prompt into independent subtasks, then starts one
/// <see cref="ComplexTaskWorkerWorkflow"/> child per task in parallel. Awaits
/// completions one-at-a-time via <see cref="Workflow.WhenAnyAsync"/>, updating
/// the <see cref="TasksCompleted"/> query field as each child finishes. A
/// <see cref="CancelAllTasksAsync"/> signal cancels all remaining children and
/// throws <see cref="ApplicationFailureException"/> of type
/// <c>OrchestrationCancelled</c>.
/// </summary>
/// <remarks>
/// Each child is started with its own <see cref="CancellationTokenSource"/>
/// linked to <see cref="Workflow.CancellationToken"/>. This lets us cancel
/// individual remaining children on the cancel signal without tearing down
/// the whole workflow. See the Temporalio SDK README — "Invoking Child
/// Workflows" — for the token-based cancellation pattern.
/// </remarks>
[Workflow]
public class OrchestrateComplexPathWorkflow
{
    private int _tasksCompleted;
    private int _tasksTotal;
    private bool _cancellationRequested;

    [WorkflowQuery]
    public int TasksRemaining => _tasksTotal - _tasksCompleted;

    [WorkflowQuery]
    public int TasksCompleted => _tasksCompleted;

    [WorkflowSignal]
    public Task CancelAllTasksAsync()
    {
        _cancellationRequested = true;
        return Task.CompletedTask;
    }

    [WorkflowRun]
    public async Task<OrchestrateComplexOutput> RunAsync(OrchestrateComplexInput input)
    {
        // Container-lifecycle branching mirrors SimpleAgentWorkflow (Fix #2).
        // Top-level HTTP dispatches send ContainerId="" — we spawn our own.
        // Nested invocation from FullOrchestrateWorkflow provides the parent's
        // container id so Architect + per-subtask workers share it.
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
            // Step 1 — decompose the prompt into independent subtasks.
            var architectInput = new ArchitectInput(
                Prompt: input.Prompt,
                ContainerId: containerId,
                GapContext: input.GapContext,
                AiAssistant: input.AiAssistant,
                SessionId: input.SessionId);

            var plan = await Workflow.ExecuteActivityAsync(
                (AiActivities a) => a.ArchitectAsync(architectInput),
                ActivityProfiles.Medium);

            _tasksTotal = plan.TaskCount;

            // Step 2 — dispatch a ComplexTaskWorkerWorkflow per task in parallel.
            // Each child gets its own CancellationTokenSource linked to the workflow
            // token so an individual child can be cancelled without cancelling the
            // parent.
            var children =
                new List<(ChildWorkflowHandle<ComplexTaskWorkerWorkflow, ComplexTaskOutput> handle,
                          CancellationTokenSource cts)>();
            foreach (var task in plan.Tasks)
            {
                var childInput = new ComplexTaskInput(
                    TaskId: task.Id,
                    Description: task.Description,
                    DependsOn: task.DependsOn,
                    FilesTouched: task.FilesTouched,
                    ContainerId: containerId,
                    AiAssistant: input.AiAssistant,
                    Model: input.Model,
                    ModelPower: input.ModelPower,
                    WorkspacePath: input.WorkspacePath,
                    ParentSessionId: input.SessionId);

                var cts = CancellationTokenSource.CreateLinkedTokenSource(
                    Workflow.CancellationToken);

                var options = new ChildWorkflowOptions
                {
                    Id = $"{input.SessionId}-task-{task.Id}",
                    ParentClosePolicy = ParentClosePolicy.Terminate,
                    CancellationToken = cts.Token
                };

                var handle = await Workflow.StartChildWorkflowAsync(
                    (ComplexTaskWorkerWorkflow w) => w.RunAsync(childInput),
                    options);
                children.Add((handle, cts));
            }

            // Step 3 — pair each handle with its in-flight Task<TResult> so we can
            // associate a completed Task back to its (handle, cts) tuple after
            // WhenAny. We mutate the working list as we consume completions.
            var working = children
                .Select(c => (handle: c.handle, cts: c.cts, task: c.handle.GetResultAsync()))
                .ToList();

            while (working.Count > 0)
            {
                if (_cancellationRequested)
                {
                    // Cancel all remaining children individually; each token is
                    // linked to Workflow.CancellationToken but cancelled here
                    // in isolation so the parent's CancellationToken stays uncancelled.
                    foreach (var (_, cts, _) in working)
                        cts.Cancel();

                    throw new ApplicationFailureException(
                        "Orchestration cancelled by signal",
                        errorType: "OrchestrationCancelled");
                }

                var completed = await Workflow.WhenAnyAsync(working.Select(p => p.task));
                // Remove the entry whose task just completed. Reference equality
                // suffices — WhenAnyAsync returns one of the Task instances we
                // passed in.
                working.RemoveAll(p => ReferenceEquals(p.task, completed));
                _tasksCompleted++;
            }

            // Step 4 — collect results. All handles have completed; GetResultAsync
            // is already-resolved on each.
            var results = new List<ComplexTaskOutput>(children.Count);
            foreach (var (handle, _) in children)
                results.Add(await handle.GetResultAsync());

            return new OrchestrateComplexOutput(
                TaskCount: _tasksTotal,
                Results: results,
                TotalCostUsd: results.Sum(r => r.CostUsd));
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
