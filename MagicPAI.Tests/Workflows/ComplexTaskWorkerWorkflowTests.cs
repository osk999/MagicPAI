using FluentAssertions;
using Temporalio.Activities;
using Temporalio.Testing;
using Temporalio.Worker;
using MagicPAI.Activities.Contracts;
using MagicPAI.Server.Workflows;
using MagicPAI.Workflows.Contracts;

namespace MagicPAI.Tests.Workflows;

/// <summary>
/// Integration tests for the Temporal <see cref="ComplexTaskWorkerWorkflow"/>.
/// Stubs <c>ClaimFile</c>, <c>ReleaseFile</c>, <c>RunCliAgent</c> so we can
/// drive claim/conflict paths without touching the real blackboard or Docker.
/// Time-skipping environment fast-forwards the 30 s retry wait automatically.
/// </summary>
[Trait("Category", "Integration")]
public class ComplexTaskWorkerWorkflowTests : IAsyncLifetime
{
    private WorkflowEnvironment _env = null!;

    public async Task InitializeAsync()
    {
        _env = await WorkflowEnvironment.StartTimeSkippingAsync();
    }

    public async Task DisposeAsync()
    {
        if (_env is not null)
            await _env.ShutdownAsync();
    }

    /// <summary>
    /// Happy path — all files claim on first try, agent runs successfully,
    /// files release in finally. Assert one claim per file, one run, one
    /// release per file.
    /// </summary>
    [Fact]
    public async Task Completes_HappyPath_AllFilesClaimAndRelease()
    {
        var stubs = new ComplexTaskStubs();

        using var worker = new TemporalWorker(
            _env.Client,
            new TemporalWorkerOptions($"test-ctw-happy-{Guid.NewGuid():N}")
                .AddAllActivities(stubs)
                .AddWorkflow<ComplexTaskWorkerWorkflow>());

        await worker.ExecuteAsync(async () =>
        {
            var input = new ComplexTaskInput(
                TaskId: "task-1",
                Description: "do the thing",
                DependsOn: Array.Empty<string>(),
                FilesTouched: new[] { "a.cs", "b.cs" },
                ContainerId: "cid-1",
                AiAssistant: "claude",
                Model: null,
                ModelPower: 2,
                WorkspacePath: "/workspace",
                ParentSessionId: "parent-1");

            var handle = await _env.Client.StartWorkflowAsync(
                (ComplexTaskWorkerWorkflow wf) => wf.RunAsync(input),
                new(id: $"ctw-happy-{Guid.NewGuid():N}",
                    taskQueue: worker.Options.TaskQueue!));

            var result = await handle.GetResultAsync();

            result.Success.Should().BeTrue();
            result.TaskId.Should().Be("task-1");
            result.Response.Should().Be("agent-response");
            result.CostUsd.Should().Be(0.15m);
            result.FilesModified.Should().BeEquivalentTo(new[] { "a.cs" });
            // Gates pass on the happy path → no repair attempt, verification true.
            result.VerificationPassed.Should().BeTrue();

            stubs.ClaimCallCount.Should().Be(2);     // one per file
            stubs.RunCliAgentCallCount.Should().Be(1);
            stubs.RunGatesCallCount.Should().Be(1);  // one post-agent gate run, no repair
            stubs.ReleasedFiles.Should().BeEquivalentTo(new[] { "a.cs", "b.cs" });

            await ReplayFixtureHelper.CaptureIfMissingAsync(
                handle, "complex-task-worker", "happy-path-v1.json");
        });
    }

    /// <summary>
    /// Conflict path — first claim attempt on <c>b.cs</c> fails, second
    /// attempt (after the 30 s delay) succeeds. Agent still runs, files
    /// release normally.
    /// </summary>
    [Fact]
    public async Task RetriesClaim_OnConflict_ThenSucceeds()
    {
        var claimCalls = new Dictionary<string, int>();
        var stubs = new ComplexTaskStubs
        {
            ClaimResponder = i =>
            {
                claimCalls.TryGetValue(i.FilePath, out var n);
                claimCalls[i.FilePath] = n + 1;
                // b.cs fails on attempt 1, wins on attempt 2.
                if (i.FilePath == "b.cs" && n == 0)
                    return new ClaimFileOutput(Claimed: false, CurrentOwner: "task-other");
                return new ClaimFileOutput(Claimed: true, CurrentOwner: null);
            }
        };

        using var worker = new TemporalWorker(
            _env.Client,
            new TemporalWorkerOptions($"test-ctw-retry-{Guid.NewGuid():N}")
                .AddAllActivities(stubs)
                .AddWorkflow<ComplexTaskWorkerWorkflow>());

        await worker.ExecuteAsync(async () =>
        {
            var input = new ComplexTaskInput(
                TaskId: "task-2",
                Description: "edit b",
                DependsOn: Array.Empty<string>(),
                FilesTouched: new[] { "a.cs", "b.cs" },
                ContainerId: "cid-1",
                AiAssistant: "claude",
                Model: null,
                ModelPower: 2,
                WorkspacePath: "/workspace",
                ParentSessionId: "parent-2");

            var handle = await _env.Client.StartWorkflowAsync(
                (ComplexTaskWorkerWorkflow wf) => wf.RunAsync(input),
                new(id: $"ctw-retry-{Guid.NewGuid():N}",
                    taskQueue: worker.Options.TaskQueue!));

            var result = await handle.GetResultAsync();

            result.Success.Should().BeTrue();
            result.VerificationPassed.Should().BeTrue();
            stubs.RunCliAgentCallCount.Should().Be(1);
            stubs.RunGatesCallCount.Should().Be(1);
            claimCalls["a.cs"].Should().Be(1);
            claimCalls["b.cs"].Should().Be(2);    // one fail + one retry win
            stubs.ReleasedFiles.Should().BeEquivalentTo(new[] { "a.cs", "b.cs" });
        });
    }

    /// <summary>
    /// Regression guard for the lock-race silent-dropout fix. Before the fix,
    /// ComplexTaskWorker retried claiming a file exactly ONCE and then dropped
    /// the task without running the agent — any sibling holding the file for
    /// longer than 30 s would cause the task to silently no-op. The fix
    /// changes this to a bounded-budget exponential-backoff wait (up to
    /// 30 minutes). This test simulates a sibling that releases the file
    /// only on the 5th attempt and asserts the task:
    /// 1) eventually acquires the lock,
    /// 2) actually runs RunCliAgent,
    /// 3) reports Success.
    /// </summary>
    [Fact]
    public async Task RetriesClaim_ManyTimes_ThenSucceeds()
    {
        var claimCalls = new Dictionary<string, int>();
        var stubs = new ComplexTaskStubs
        {
            ClaimResponder = i =>
            {
                claimCalls.TryGetValue(i.FilePath, out var n);
                claimCalls[i.FilePath] = n + 1;
                // b.cs fails the first 4 attempts, wins on the 5th.
                if (i.FilePath == "b.cs" && n < 4)
                    return new ClaimFileOutput(Claimed: false, CurrentOwner: "busy-sibling");
                return new ClaimFileOutput(Claimed: true, CurrentOwner: null);
            }
        };

        using var worker = new TemporalWorker(
            _env.Client,
            new TemporalWorkerOptions($"test-ctw-manyretry-{Guid.NewGuid():N}")
                .AddAllActivities(stubs)
                .AddWorkflow<ComplexTaskWorkerWorkflow>());

        await worker.ExecuteAsync(async () =>
        {
            var input = new ComplexTaskInput(
                TaskId: "task-many",
                Description: "edit b",
                DependsOn: Array.Empty<string>(),
                FilesTouched: new[] { "a.cs", "b.cs" },
                ContainerId: "cid-1",
                AiAssistant: "claude",
                Model: null,
                ModelPower: 2,
                WorkspacePath: "/workspace",
                ParentSessionId: "parent-many");

            var handle = await _env.Client.StartWorkflowAsync(
                (ComplexTaskWorkerWorkflow wf) => wf.RunAsync(input),
                new(id: $"ctw-many-{Guid.NewGuid():N}",
                    taskQueue: worker.Options.TaskQueue!));

            var result = await handle.GetResultAsync();

            result.Success.Should().BeTrue("worker should keep retrying until the sibling releases");
            stubs.RunCliAgentCallCount.Should().Be(1, "agent must run after the lock is finally acquired — this is the whole point of the fix");
            claimCalls["b.cs"].Should().Be(5, "4 failures + 1 winning attempt");
            stubs.ReleasedFiles.Should().BeEquivalentTo(new[] { "a.cs", "b.cs" });
        });
    }

    /// <summary>
    /// Conflict-permanent path — claim fails on first and second attempts.
    /// Workflow returns without running the agent, Success=false, and still
    /// releases any files it had claimed earlier.
    /// </summary>
    [Fact]
    public async Task ReturnsFailure_WhenClaimConflictPersists()
    {
        var stubs = new ComplexTaskStubs
        {
            // a.cs always claims OK; b.cs always fails.
            ClaimResponder = i =>
                i.FilePath == "b.cs"
                    ? new ClaimFileOutput(Claimed: false, CurrentOwner: "task-other")
                    : new ClaimFileOutput(Claimed: true, CurrentOwner: null)
        };

        using var worker = new TemporalWorker(
            _env.Client,
            new TemporalWorkerOptions($"test-ctw-fail-{Guid.NewGuid():N}")
                .AddAllActivities(stubs)
                .AddWorkflow<ComplexTaskWorkerWorkflow>());

        await worker.ExecuteAsync(async () =>
        {
            var input = new ComplexTaskInput(
                TaskId: "task-3",
                Description: "edit b",
                DependsOn: Array.Empty<string>(),
                FilesTouched: new[] { "a.cs", "b.cs" },
                ContainerId: "cid-1",
                AiAssistant: "claude",
                Model: null,
                ModelPower: 2,
                WorkspacePath: "/workspace",
                ParentSessionId: "parent-3");

            var handle = await _env.Client.StartWorkflowAsync(
                (ComplexTaskWorkerWorkflow wf) => wf.RunAsync(input),
                new(id: $"ctw-fail-{Guid.NewGuid():N}",
                    taskQueue: worker.Options.TaskQueue!));

            var result = await handle.GetResultAsync();

            result.Success.Should().BeFalse();
            result.TaskId.Should().Be("task-3");
            result.CostUsd.Should().Be(0m);
            result.Response.Should().Contain("b.cs").And.Contain("task-other");
            // New-behaviour message includes the budget-exhausted reason.
            result.Response.Should().Contain("giving up");
            result.FilesModified.Should().BeEmpty();
            // Claim-conflict exits BEFORE agent run → verification never happens.
            // Default ComplexTaskOutput.VerificationPassed is true.
            result.VerificationPassed.Should().BeTrue();

            stubs.RunCliAgentCallCount.Should().Be(0);
            stubs.RunGatesCallCount.Should().Be(0);
            // a.cs was claimed; on failure we release the listed FilesTouched
            // (ReleaseFile is ownership-checked so b.cs release is a no-op).
            stubs.ReleaseCallCount.Should().BeGreaterThan(0);
        });
    }

    /// <summary>
    /// Repair path — first verify fails, generate repair prompt, re-run agent,
    /// second verify passes. Asserts: 2 RunCliAgent calls (initial + repair),
    /// 2 RunGates calls (pre- + post-repair), 1 GenerateRepairPrompt call,
    /// VerificationPassed=true in the final output.
    /// </summary>
    [Fact]
    public async Task RunsOneRepairIteration_WhenFirstVerifyFails()
    {
        var verifyCalls = 0;
        var stubs = new ComplexTaskStubs
        {
            VerifyResponder = _ =>
            {
                var n = Interlocked.Increment(ref verifyCalls);
                return n == 1
                    ? new VerifyOutput(
                        AllPassed: false,
                        FailedGates: new[] { "compile" },
                        GateResultsJson: "[{\"gate\":\"compile\",\"ok\":false}]")
                    : new VerifyOutput(
                        AllPassed: true,
                        FailedGates: Array.Empty<string>(),
                        GateResultsJson: "[]");
            }
        };

        using var worker = new TemporalWorker(
            _env.Client,
            new TemporalWorkerOptions($"test-ctw-repair-{Guid.NewGuid():N}")
                .AddAllActivities(stubs)
                .AddWorkflow<ComplexTaskWorkerWorkflow>());

        await worker.ExecuteAsync(async () =>
        {
            var input = new ComplexTaskInput(
                TaskId: "task-4",
                Description: "implement",
                DependsOn: Array.Empty<string>(),
                FilesTouched: new[] { "a.cs" },
                ContainerId: "cid-1",
                AiAssistant: "claude",
                Model: null,
                ModelPower: 2,
                WorkspacePath: "/workspace",
                ParentSessionId: "parent-4");

            var handle = await _env.Client.StartWorkflowAsync(
                (ComplexTaskWorkerWorkflow wf) => wf.RunAsync(input),
                new(id: $"ctw-repair-{Guid.NewGuid():N}",
                    taskQueue: worker.Options.TaskQueue!));

            var result = await handle.GetResultAsync();

            result.Success.Should().BeTrue();
            result.VerificationPassed.Should().BeTrue();
            stubs.RunCliAgentCallCount.Should().Be(2);  // initial + repair
            stubs.RunGatesCallCount.Should().Be(2);     // initial + post-repair
            stubs.GenerateRepairPromptCallCount.Should().Be(1);
            // CostUsd accumulates: 0.15 (initial) + 0.15 (repair) = 0.30.
            result.CostUsd.Should().Be(0.30m);
        });
    }

    /// <summary>
    /// Stub bag implementing the activities used by
    /// <see cref="ComplexTaskWorkerWorkflow"/>: <c>ClaimFile</c>, <c>ReleaseFile</c>,
    /// <c>RunCliAgent</c>, <c>RunGates</c>, <c>GenerateRepairPrompt</c>. Activity
    /// names strip "Async" off the method name so the stubs register as
    /// "ClaimFile", "ReleaseFile", "RunCliAgent", "RunGates",
    /// "GenerateRepairPrompt" — matching the names the workflow resolves through
    /// Workflow.ExecuteActivityAsync.
    /// </summary>
    public class ComplexTaskStubs
    {
        public Func<ClaimFileInput, ClaimFileOutput> ClaimResponder { get; set; } =
            _ => new ClaimFileOutput(Claimed: true, CurrentOwner: null);

        public Func<RunCliAgentInput, RunCliAgentOutput> RunResponder { get; set; } =
            _ => new RunCliAgentOutput(
                Response: "agent-response",
                StructuredOutputJson: null,
                Success: true,
                CostUsd: 0.15m,
                InputTokens: 10,
                OutputTokens: 20,
                FilesModified: new[] { "a.cs" },
                ExitCode: 0,
                AssistantSessionId: "stub-session");

        // Default: verify always passes, so the happy path exercises a single
        // post-agent gate run without a repair attempt.
        public Func<VerifyInput, VerifyOutput> VerifyResponder { get; set; } =
            _ => new VerifyOutput(
                AllPassed: true,
                FailedGates: Array.Empty<string>(),
                GateResultsJson: "[]");

        public Func<RepairInput, RepairOutput> RepairResponder { get; set; } =
            _ => new RepairOutput(
                RepairPrompt: "Fix the compile error.",
                ShouldAttemptRepair: true);

        // Counters may be incremented from concurrent activity executions —
        // Interlocked avoids lost updates.
        private int _claimCalls;
        private int _releaseCalls;
        private int _runCliAgentCalls;
        private int _runGatesCalls;
        private int _generateRepairPromptCalls;

        public int ClaimCallCount => _claimCalls;
        public int ReleaseCallCount => _releaseCalls;
        public int RunCliAgentCallCount => _runCliAgentCalls;
        public int RunGatesCallCount => _runGatesCalls;
        public int GenerateRepairPromptCallCount => _generateRepairPromptCalls;
        public List<string> ReleasedFiles { get; } = new();

        [Activity]
        public Task<ClaimFileOutput> ClaimFileAsync(ClaimFileInput i)
        {
            Interlocked.Increment(ref _claimCalls);
            return Task.FromResult(ClaimResponder(i));
        }

        [Activity]
        public Task ReleaseFileAsync(ReleaseFileInput i)
        {
            Interlocked.Increment(ref _releaseCalls);
            lock (ReleasedFiles) { ReleasedFiles.Add(i.FilePath); }
            return Task.CompletedTask;
        }

        [Activity]
        public Task<RunCliAgentOutput> RunCliAgentAsync(RunCliAgentInput i)
        {
            Interlocked.Increment(ref _runCliAgentCalls);
            return Task.FromResult(RunResponder(i));
        }

        [Activity]
        public Task<VerifyOutput> RunGatesAsync(VerifyInput i)
        {
            Interlocked.Increment(ref _runGatesCalls);
            return Task.FromResult(VerifyResponder(i));
        }

        [Activity]
        public Task<RepairOutput> GenerateRepairPromptAsync(RepairInput i)
        {
            Interlocked.Increment(ref _generateRepairPromptCalls);
            return Task.FromResult(RepairResponder(i));
        }
    }
}
