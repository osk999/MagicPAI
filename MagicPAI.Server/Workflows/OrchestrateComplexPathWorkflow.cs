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
using MagicPAI.Activities.Git;
using MagicPAI.Activities.Stage;
using MagicPAI.Workflows;
using MagicPAI.Workflows.Contracts;

namespace MagicPAI.Server.Workflows;

/// <summary>
/// Orchestrates the "complex" execution path. Calls the Architect activity to
/// decompose the prompt into independent subtasks, then starts one
/// <see cref="ComplexTaskWorkerWorkflow"/> child per task. Awaits completions
/// one-at-a-time via <see cref="Workflow.WhenAnyAsync"/>, updating the
/// <see cref="TasksCompleted"/> query field as each child finishes. A
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
    private decimal _totalCost;

    [WorkflowQuery]
    public int TasksRemaining => _tasksTotal - _tasksCompleted;

    [WorkflowQuery]
    public int TasksCompleted => _tasksCompleted;

    /// <summary>
    /// Running total cost across the architect activity, all child workers'
    /// returned costs, and any post-merge work. Phase-1 gap: this query was
    /// missing so Studio's cost tile stayed at zero through the run.
    /// </summary>
    [WorkflowQuery]
    public decimal TotalCostUsd => _totalCost;

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
            await EmitStageAsync(input.SessionId, "architecting");

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

            await EmitStageAsync(input.SessionId, "task-fanout");

            // Per-task working state — workspace path (worktree or shared),
            // child handle and cancellation token. Used by both the legacy
            // fan-out-all branch and the new DAG-ordered branch.
            var perTaskWorkspace = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var task in plan.Tasks)
                perTaskWorkspace[task.Id] = input.WorkspacePath;

            // Optionally create a per-task worktree so parallel ComplexTaskWorker
            // children stop racing on the bind-mounted FS. Wrapped in a patch
            // gate because old replays didn't schedule CreateWorktree activities.
            if (Workflow.Patched("complex-path-worktree-v1"))
            {
                foreach (var task in plan.Tasks)
                {
                    // Sanitize so user-supplied task ids can't escape the
                    // branch-name shell-safe character set.
                    var branch = $"task/{GitActivities.SanitizeBranchName(task.Id)}";
                    var createInput = new CreateWorktreeInput(
                        ContainerId: containerId,
                        BranchName: branch,
                        RepoDirectory: input.RepoDirectory,
                        BaseBranch: input.BaseBranch);

                    var worktree = await Workflow.ExecuteActivityAsync(
                        (GitActivities g) => g.CreateWorktreeAsync(createInput),
                        ActivityProfiles.Short);

                    perTaskWorkspace[task.Id] = worktree.WorktreePath;
                }
            }

            // Step 2 — DAG-ordered fan-out (respect task.DependsOn) gated by a
            // patch so old histories with the legacy fan-out-all sequence still
            // replay.
            List<ComplexTaskOutput> results;
            if (Workflow.Patched("complex-path-dag-ordering-v1"))
            {
                results = await RunTasksDagOrderedAsync(input, containerId, plan, perTaskWorkspace);
            }
            else
            {
                results = await RunTasksFanOutAllAsync(input, containerId, plan, perTaskWorkspace);
            }

            // Sum child costs into the running total + broadcast.
            foreach (var r in results)
                _totalCost += r.CostUsd;
            await EmitCostAsync(input.SessionId, _totalCost);

            // Step 3 — merge worktrees back to the base branch. Conflicts are
            // logged as a "merge-conflict" stage and DO NOT throw — the parent
            // VerifyAndRepair loop will pick the issue up.
            if (Workflow.Patched("complex-path-worktree-merge-v1"))
            {
                await EmitStageAsync(input.SessionId, "merging");

                foreach (var task in plan.Tasks)
                {
                    var branch = $"task/{GitActivities.SanitizeBranchName(task.Id)}";

                    var mergeInput = new MergeWorktreeInput(
                        ContainerId: containerId,
                        BranchName: branch,
                        RepoDirectory: input.RepoDirectory,
                        TargetBranch: input.BaseBranch,
                        PushAfterMerge: false);

                    var merge = await Workflow.ExecuteActivityAsync(
                        (GitActivities g) => g.MergeWorktreeAsync(mergeInput),
                        ActivityProfiles.Short);

                    if (!merge.Merged)
                        await EmitStageAsync(input.SessionId, $"merge-conflict-{task.Id}");

                    var cleanupInput = new CleanupWorktreeInput(
                        ContainerId: containerId,
                        BranchName: branch,
                        RepoDirectory: input.RepoDirectory,
                        DeleteBranch: false);

                    await Workflow.ExecuteActivityAsync(
                        (GitActivities g) => g.CleanupWorktreeAsync(cleanupInput),
                        ActivityProfiles.Short);
                }
            }

            await EmitStageAsync(input.SessionId, "done");

            return new OrchestrateComplexOutput(
                TaskCount: _tasksTotal,
                Results: results,
                TotalCostUsd: _totalCost);
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
    /// Legacy fan-out-all behaviour. Starts every child workflow up front and
    /// awaits completions in any order. Kept behind the
    /// <c>complex-path-dag-ordering-v1</c> patch gate so old workflow histories
    /// replay deterministically.
    /// </summary>
    private async Task<List<ComplexTaskOutput>> RunTasksFanOutAllAsync(
        OrchestrateComplexInput input,
        string containerId,
        ArchitectOutput plan,
        Dictionary<string, string> perTaskWorkspace)
    {
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
                WorkspacePath: perTaskWorkspace[task.Id],
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
        return results;
    }

    /// <summary>
    /// DAG-ordered fan-out. Respects each task's <see cref="TaskPlanEntry.DependsOn"/>
    /// list and caps concurrent children at <see cref="OrchestrateComplexInput.MaxConcurrentWorkers"/>.
    /// Tasks become startable once all their declared deps have completed.
    /// Deadlocks (cycle / unsatisfiable deps) break out cleanly with a logged
    /// stage emit, leaving the rest of the orchestration to verify-and-repair.
    /// </summary>
    private async Task<List<ComplexTaskOutput>> RunTasksDagOrderedAsync(
        OrchestrateComplexInput input,
        string containerId,
        ArchitectOutput plan,
        Dictionary<string, string> perTaskWorkspace)
    {
        var taskById = plan.Tasks.ToDictionary(t => t.Id, t => t, StringComparer.Ordinal);
        var pending = new HashSet<string>(plan.Tasks.Select(t => t.Id), StringComparer.Ordinal);
        var completed = new Dictionary<string, ComplexTaskOutput>(StringComparer.Ordinal);
        var running = new List<RunningChild>();

        var maxConcurrent = Math.Max(1, input.MaxConcurrentWorkers);

        while (pending.Count > 0 || running.Count > 0)
        {
            if (_cancellationRequested)
            {
                foreach (var rc in running)
                    rc.Cts.Cancel();

                throw new ApplicationFailureException(
                    "Orchestration cancelled by signal",
                    errorType: "OrchestrationCancelled");
            }

            // Start as many ready-and-allowed tasks as we can.
            var slot = maxConcurrent - running.Count;
            if (slot > 0)
            {
                var ready = plan.Tasks
                    .Where(t => pending.Contains(t.Id))
                    .Where(t => t.DependsOn.All(dep => !taskById.ContainsKey(dep) || completed.ContainsKey(dep)))
                    .Take(slot)
                    .ToList();

                foreach (var task in ready)
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
                        WorkspacePath: perTaskWorkspace[task.Id],
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

                    running.Add(new RunningChild(task.Id, handle, cts, handle.GetResultAsync()));
                    pending.Remove(task.Id);
                }
            }

            if (running.Count == 0)
            {
                // No children running and no tasks startable — circular/unsatisfiable
                // deps. Break out with a logged stage emit; the parent's
                // verify-and-repair loop is the recovery mechanism.
                await EmitStageAsync(input.SessionId, "dag-deadlock");
                break;
            }

            var completedTask = await Workflow.WhenAnyAsync(running.Select(r => r.Task));
            var idx = running.FindIndex(r => ReferenceEquals(r.Task, completedTask));
            var finished = running[idx];
            running.RemoveAt(idx);

            var result = await finished.Task;
            completed[finished.TaskId] = result;
            _tasksCompleted++;
        }

        // Preserve plan.Tasks ordering in the output for consistency with the
        // legacy fan-out branch.
        var results = new List<ComplexTaskOutput>(plan.Tasks.Count);
        foreach (var t in plan.Tasks)
            if (completed.TryGetValue(t.Id, out var r))
                results.Add(r);
        return results;
    }

    private sealed record RunningChild(
        string TaskId,
        ChildWorkflowHandle<ComplexTaskWorkerWorkflow, ComplexTaskOutput> Handle,
        CancellationTokenSource Cts,
        Task<ComplexTaskOutput> Task);

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
    /// Broadcast running total cost. Gated on
    /// <c>Workflow.Patched("emit-cost-activity-v1")</c> for replay safety.
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
