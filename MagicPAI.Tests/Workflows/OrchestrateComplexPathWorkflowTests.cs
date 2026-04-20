using FluentAssertions;
using Temporalio.Activities;
using Temporalio.Testing;
using Temporalio.Worker;
using MagicPAI.Activities.Contracts;
using MagicPAI.Server.Workflows;
using MagicPAI.Workflows.Contracts;

namespace MagicPAI.Tests.Workflows;

/// <summary>
/// Integration tests for the Temporal <see cref="OrchestrateComplexPathWorkflow"/>.
/// Registers both the orchestrator and <see cref="ComplexTaskWorkerWorkflow"/> on
/// the same test worker, stubs the activities (Architect + ClaimFile / ReleaseFile /
/// RunCliAgent) and asserts fan-out + result aggregation. Uses time-skipping so
/// the conflict-retry delay in children never actually blocks the test.
/// </summary>
[Trait("Category", "Integration")]
public class OrchestrateComplexPathWorkflowTests : IAsyncLifetime
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
    /// Happy path — Architect returns a 3-task plan, each child runs once and
    /// succeeds. Asserts aggregate task count, total cost, and that each per-task
    /// result is surfaced in the output list.
    /// </summary>
    [Fact]
    public async Task Completes_HappyPath_WithThreeTaskPlan()
    {
        var stubs = new OrchestrateComplexStubs
        {
            ArchitectResponder = _ =>
            {
                var tasks = new[]
                {
                    new TaskPlanEntry(
                        Id: "t1", Description: "do 1",
                        DependsOn: Array.Empty<string>(), FilesTouched: new[] { "a.cs" }),
                    new TaskPlanEntry(
                        Id: "t2", Description: "do 2",
                        DependsOn: Array.Empty<string>(), FilesTouched: new[] { "b.cs" }),
                    new TaskPlanEntry(
                        Id: "t3", Description: "do 3",
                        DependsOn: Array.Empty<string>(), FilesTouched: new[] { "c.cs" }),
                };
                return new ArchitectOutput(
                    TaskListJson: "[]",
                    TaskCount: tasks.Length,
                    Tasks: tasks);
            },
            RunResponder = i => new RunCliAgentOutput(
                Response: $"done-{i.Prompt}",
                StructuredOutputJson: null,
                Success: true,
                CostUsd: 0.10m,
                InputTokens: 10,
                OutputTokens: 20,
                FilesModified: Array.Empty<string>(),
                ExitCode: 0,
                AssistantSessionId: "stub"),
        };

        using var worker = new TemporalWorker(
            _env.Client,
            new TemporalWorkerOptions($"test-orch-complex-{Guid.NewGuid():N}")
                .AddAllActivities(stubs)
                .AddWorkflow<OrchestrateComplexPathWorkflow>()
                .AddWorkflow<ComplexTaskWorkerWorkflow>());

        await worker.ExecuteAsync(async () =>
        {
            var input = new OrchestrateComplexInput(
                SessionId: "orch-complex-happy",
                Prompt: "big task",
                ContainerId: "cid-1",
                WorkspacePath: "/workspace",
                AiAssistant: "claude",
                Model: null,
                ModelPower: 2);

            var handle = await _env.Client.StartWorkflowAsync(
                (OrchestrateComplexPathWorkflow wf) => wf.RunAsync(input),
                new(id: $"orch-complex-happy-{Guid.NewGuid():N}",
                    taskQueue: worker.Options.TaskQueue!));

            var result = await handle.GetResultAsync();

            result.TaskCount.Should().Be(3);
            result.Results.Should().HaveCount(3);
            result.Results.Select(r => r.TaskId).Should().BeEquivalentTo(new[] { "t1", "t2", "t3" });
            result.Results.Should().OnlyContain(r => r.Success);
            result.TotalCostUsd.Should().Be(0.30m); // 3 tasks x $0.10

            stubs.ArchitectCallCount.Should().Be(1);
            stubs.RunCliAgentCallCount.Should().Be(3);
            stubs.ClaimCallCount.Should().Be(3);   // one per file across 3 tasks

            await ReplayFixtureHelper.CaptureIfMissingAsync(
                handle, "orchestrate-complex-path", "happy-path-v1.json");
        });
    }

    /// <summary>
    /// Stub bag covering all activities the orchestrator + child touch:
    /// Architect + ClaimFile + ReleaseFile + RunCliAgent. Activity-name rule:
    /// method name minus "Async" suffix. These register as "Architect",
    /// "ClaimFile", "ReleaseFile", "RunCliAgent".
    /// </summary>
    public class OrchestrateComplexStubs
    {
        public Func<ArchitectInput, ArchitectOutput> ArchitectResponder { get; set; } =
            _ => new ArchitectOutput(
                TaskListJson: "[]",
                TaskCount: 0,
                Tasks: Array.Empty<TaskPlanEntry>());

        public Func<ClaimFileInput, ClaimFileOutput> ClaimResponder { get; set; } =
            _ => new ClaimFileOutput(Claimed: true, CurrentOwner: null);

        public Func<RunCliAgentInput, RunCliAgentOutput> RunResponder { get; set; } =
            _ => new RunCliAgentOutput(
                Response: "stub",
                StructuredOutputJson: null,
                Success: true,
                CostUsd: 0.10m,
                InputTokens: 1,
                OutputTokens: 1,
                FilesModified: Array.Empty<string>(),
                ExitCode: 0,
                AssistantSessionId: "stub");

        // Verify always passes in these orchestrator-level tests — the
        // ComplexTaskWorker child's per-subtask verify loop is exercised
        // directly in ComplexTaskWorkerWorkflowTests.
        public Func<VerifyInput, VerifyOutput> VerifyResponder { get; set; } =
            _ => new VerifyOutput(
                AllPassed: true,
                FailedGates: Array.Empty<string>(),
                GateResultsJson: "[]");

        // Counters are updated from concurrent activity executions (fan-out);
        // Interlocked avoids lost updates when 3 children run in parallel.
        private int _architectCalls;
        private int _claimCalls;
        private int _releaseCalls;
        private int _runCliAgentCalls;
        private int _runGatesCalls;

        public int ArchitectCallCount => _architectCalls;
        public int ClaimCallCount => _claimCalls;
        public int ReleaseCallCount => _releaseCalls;
        public int RunCliAgentCallCount => _runCliAgentCalls;
        public int RunGatesCallCount => _runGatesCalls;

        [Activity]
        public Task<ArchitectOutput> ArchitectAsync(ArchitectInput i)
        {
            Interlocked.Increment(ref _architectCalls);
            return Task.FromResult(ArchitectResponder(i));
        }

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
    }
}
