using FluentAssertions;
using Temporalio.Activities;
using Temporalio.Testing;
using Temporalio.Worker;
using MagicPAI.Activities.Contracts;
using MagicPAI.Server.Workflows;
using MagicPAI.Workflows.Contracts;

namespace MagicPAI.Tests.Workflows;

/// <summary>
/// Integration tests for the Temporal <see cref="VerifyAndRepairWorkflow"/> — the
/// reusable verify + repair loop. Uses <see cref="WorkflowEnvironment.StartTimeSkippingAsync"/>
/// + stubbed <c>VerifyActivities</c> / <c>AiActivities</c> so the whole loop runs
/// without touching Docker or Claude. Pattern matches
/// <see cref="SimpleAgentWorkflowTests"/>. See temporal.md §RR.5 / §RR.7.
/// </summary>
[Trait("Category", "Integration")]
public class VerifyAndRepairWorkflowTests : IAsyncLifetime
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
    /// Happy path — the first verify call returns <c>AllPassed=true</c> so the
    /// workflow exits with zero repair attempts and no rerun.
    /// </summary>
    [Fact]
    public async Task Succeeds_WhenGatesPassOnFirstAttempt()
    {
        var stubs = new VerifyAndRepairStubs
        {
            VerifyResponder = _ => new VerifyOutput(
                AllPassed: true,
                FailedGates: Array.Empty<string>(),
                GateResultsJson: "[]")
        };

        using var worker = new TemporalWorker(
            _env.Client,
            new TemporalWorkerOptions($"test-vr-happy-{Guid.NewGuid():N}")
                .AddAllActivities(stubs)
                .AddAllActivities(new StageActivityStubs())
                .AddWorkflow<VerifyAndRepairWorkflow>());

        await worker.ExecuteAsync(async () =>
        {
            var input = new VerifyAndRepairInput(
                SessionId: "vr-happy",
                ContainerId: "fake-cid",
                WorkingDirectory: "/workspace",
                OriginalPrompt: "write code",
                AiAssistant: "claude",
                Model: null,
                ModelPower: 2,
                Gates: new[] { "compile", "test" },
                WorkerOutput: "original output");

            var handle = await _env.Client.StartWorkflowAsync(
                (VerifyAndRepairWorkflow wf) => wf.RunAsync(input),
                new(id: $"vr-happy-{Guid.NewGuid():N}", taskQueue: worker.Options.TaskQueue!));

            var result = await handle.GetResultAsync();

            result.Success.Should().BeTrue();
            result.RepairAttempts.Should().Be(0);
            result.FinalFailedGates.Should().BeEmpty();
            result.RepairCostUsd.Should().Be(0m);

            stubs.VerifyCallCount.Should().Be(1);
            stubs.RepairPromptCallCount.Should().Be(0);
            stubs.RunCliAgentCallCount.Should().Be(0);

            await ReplayFixtureHelper.CaptureIfMissingAsync(
                handle, "verify-and-repair", "happy-path-v1.json");
        });
    }

    /// <summary>
    /// Repair loop — first verify call fails, second succeeds after a repair.
    /// Asserts the workflow ran exactly one repair iteration and accumulated the
    /// rerun cost.
    /// </summary>
    [Fact]
    public async Task Repairs_ThenSucceeds_OnSecondVerify()
    {
        var verifyCalls = 0;
        var stubs = new VerifyAndRepairStubs
        {
            VerifyResponder = _ =>
            {
                verifyCalls++;
                return verifyCalls switch
                {
                    1 => new VerifyOutput(
                        AllPassed: false,
                        FailedGates: new[] { "compile" },
                        GateResultsJson: "[{\"name\":\"compile\",\"passed\":false}]"),
                    _ => new VerifyOutput(
                        AllPassed: true,
                        FailedGates: Array.Empty<string>(),
                        GateResultsJson: "[]")
                };
            },
            RunResponder = _ => new RunCliAgentOutput(
                Response: "fixed",
                StructuredOutputJson: null,
                Success: true,
                CostUsd: 0.25m,
                InputTokens: 10,
                OutputTokens: 20,
                FilesModified: new[] { "a.cs" },
                ExitCode: 0,
                AssistantSessionId: "stub"),
        };

        using var worker = new TemporalWorker(
            _env.Client,
            new TemporalWorkerOptions($"test-vr-repair-{Guid.NewGuid():N}")
                .AddAllActivities(stubs)
                .AddAllActivities(new StageActivityStubs())
                .AddWorkflow<VerifyAndRepairWorkflow>());

        await worker.ExecuteAsync(async () =>
        {
            var input = new VerifyAndRepairInput(
                SessionId: "vr-repair",
                ContainerId: "fake-cid",
                WorkingDirectory: "/workspace",
                OriginalPrompt: "write code",
                AiAssistant: "claude",
                Model: null,
                ModelPower: 2,
                Gates: new[] { "compile" },
                WorkerOutput: "broken");

            var handle = await _env.Client.StartWorkflowAsync(
                (VerifyAndRepairWorkflow wf) => wf.RunAsync(input),
                new(id: $"vr-repair-{Guid.NewGuid():N}", taskQueue: worker.Options.TaskQueue!));

            var result = await handle.GetResultAsync();

            result.Success.Should().BeTrue();
            result.RepairAttempts.Should().Be(1);
            result.FinalFailedGates.Should().BeEmpty();
            result.RepairCostUsd.Should().Be(0.25m);

            stubs.VerifyCallCount.Should().Be(2);
            stubs.RepairPromptCallCount.Should().Be(1);
            stubs.RunCliAgentCallCount.Should().Be(1);
        });
    }

    /// <summary>
    /// Stub bag that implements the activities invoked by
    /// <see cref="VerifyAndRepairWorkflow"/>: <c>RunGates</c>,
    /// <c>GenerateRepairPrompt</c>, <c>RunCliAgent</c>. Default responders simulate
    /// happy-path behavior; per-test overrides replace them as needed.
    /// </summary>
    public class VerifyAndRepairStubs
    {
        public Func<VerifyInput, VerifyOutput> VerifyResponder { get; set; } =
            _ => new VerifyOutput(
                AllPassed: true,
                FailedGates: Array.Empty<string>(),
                GateResultsJson: "[]");

        public Func<RepairInput, RepairOutput> RepairResponder { get; set; } =
            i => new RepairOutput(
                RepairPrompt: $"fix {string.Join(",", i.FailedGates ?? Array.Empty<string>())}",
                ShouldAttemptRepair: true);

        public Func<RunCliAgentInput, RunCliAgentOutput> RunResponder { get; set; } =
            _ => new RunCliAgentOutput(
                Response: "rerun-response",
                StructuredOutputJson: null,
                Success: true,
                CostUsd: 0.10m,
                InputTokens: 50,
                OutputTokens: 80,
                FilesModified: Array.Empty<string>(),
                ExitCode: 0,
                AssistantSessionId: "stub-session");

        public int VerifyCallCount { get; private set; }
        public int RepairPromptCallCount { get; private set; }
        public int RunCliAgentCallCount { get; private set; }

        // Activity names default to method name minus "Async" suffix, so these
        // register as "RunGates", "GenerateRepairPrompt", "RunCliAgent" — matching
        // the real signatures the workflow refers to via Workflow.ExecuteActivityAsync.
        [Activity]
        public Task<VerifyOutput> RunGatesAsync(VerifyInput i)
        {
            VerifyCallCount++;
            return Task.FromResult(VerifyResponder(i));
        }

        [Activity]
        public Task<RepairOutput> GenerateRepairPromptAsync(RepairInput i)
        {
            RepairPromptCallCount++;
            return Task.FromResult(RepairResponder(i));
        }

        [Activity]
        public Task<RunCliAgentOutput> RunCliAgentAsync(RunCliAgentInput i)
        {
            RunCliAgentCallCount++;
            return Task.FromResult(RunResponder(i));
        }
    }
}
