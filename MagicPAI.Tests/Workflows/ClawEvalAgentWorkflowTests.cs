using FluentAssertions;
using Temporalio.Activities;
using Temporalio.Testing;
using Temporalio.Worker;
using MagicPAI.Activities.Contracts;
using MagicPAI.Server.Workflows;
using MagicPAI.Workflows.Contracts;

namespace MagicPAI.Tests.Workflows;

/// <summary>
/// Integration tests for the Temporal <see cref="ClawEvalAgentWorkflow"/>. The
/// eval workflow runs on a pre-spawned container so we only stub RunCliAgent
/// and RunGates. Asserts the three-gate (compile + test + coverage) set is
/// forwarded as-is. See temporal.md §H.10.
/// </summary>
[Trait("Category", "Integration")]
public class ClawEvalAgentWorkflowTests : IAsyncLifetime
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

    [Fact]
    public async Task Completes_PassingEval_WithFullGateSet()
    {
        var stubs = new ClawEvalStubs();

        using var worker = new TemporalWorker(
            _env.Client,
            new TemporalWorkerOptions($"test-claweval-{Guid.NewGuid():N}")
                .AddAllActivities(stubs)
                .AddAllActivities(new StageActivityStubs())
                .AddWorkflow<ClawEvalAgentWorkflow>());

        await worker.ExecuteAsync(async () =>
        {
            var input = new ClawEvalAgentInput(
                SessionId: "ce-happy",
                EvalTaskId: "eval-001",
                Prompt: "solve the task",
                ContainerId: "cid-1",
                WorkspacePath: "/workspace",
                AiAssistant: "claude",
                Model: null,
                ModelPower: 2);

            var handle = await _env.Client.StartWorkflowAsync(
                (ClawEvalAgentWorkflow wf) => wf.RunAsync(input),
                new(id: $"ce-happy-{Guid.NewGuid():N}",
                    taskQueue: worker.Options.TaskQueue!));

            var result = await handle.GetResultAsync();

            result.Response.Should().Be("eval-run-response");
            result.PassedEval.Should().BeTrue();
            result.EvalReport.Should().Be("[{\"gate\":\"compile\",\"passed\":true}]");
            result.CostUsd.Should().Be(0.40m);

            // Full eval gate set is forwarded unchanged.
            stubs.LastVerifyInput.Should().NotBeNull();
            stubs.LastVerifyInput!.EnabledGates.Should()
                .BeEquivalentTo(new[] { "compile", "test", "coverage" });
            stubs.RunCliAgentCallCount.Should().Be(1);
            stubs.VerifyCallCount.Should().Be(1);

            await ReplayFixtureHelper.CaptureIfMissingAsync(
                handle, "claw-eval-agent", "happy-path-v1.json");
        });
    }

    public class ClawEvalStubs
    {
        public VerifyInput? LastVerifyInput { get; private set; }
        public int RunCliAgentCallCount { get; private set; }
        public int VerifyCallCount { get; private set; }

        public Func<RunCliAgentInput, RunCliAgentOutput> RunResponder { get; set; } =
            _ => new RunCliAgentOutput(
                Response: "eval-run-response",
                StructuredOutputJson: null,
                Success: true,
                CostUsd: 0.40m,
                InputTokens: 100,
                OutputTokens: 200,
                FilesModified: new[] { "solution.cs" },
                ExitCode: 0,
                AssistantSessionId: "stub");

        public Func<VerifyInput, VerifyOutput> VerifyResponder { get; set; } =
            _ => new VerifyOutput(
                AllPassed: true,
                FailedGates: Array.Empty<string>(),
                GateResultsJson: "[{\"gate\":\"compile\",\"passed\":true}]");

        [Activity]
        public Task<RunCliAgentOutput> RunCliAgentAsync(RunCliAgentInput i)
        {
            RunCliAgentCallCount++;
            return Task.FromResult(RunResponder(i));
        }

        [Activity]
        public Task<VerifyOutput> RunGatesAsync(VerifyInput i)
        {
            LastVerifyInput = i;
            VerifyCallCount++;
            return Task.FromResult(VerifyResponder(i));
        }
    }
}
