using FluentAssertions;
using Temporalio.Activities;
using Temporalio.Testing;
using Temporalio.Worker;
using MagicPAI.Activities.Contracts;
using MagicPAI.Server.Workflows;
using MagicPAI.Workflows.Contracts;

namespace MagicPAI.Tests.Workflows;

/// <summary>
/// Integration tests for the Temporal <see cref="PostExecutionPipelineWorkflow"/>.
/// Stubs the two activities the workflow touches (RunGates + RunCliAgent) and
/// asserts the report is surfaced back to the caller. Uses
/// <see cref="WorkflowEnvironment.StartTimeSkippingAsync"/> so test timings are
/// deterministic. See temporal.md §H.7.
/// </summary>
[Trait("Category", "Integration")]
public class PostExecutionPipelineWorkflowTests : IAsyncLifetime
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
    public async Task Completes_WithReport_WhenGatesPass()
    {
        var stubs = new PostExecStubs();

        using var worker = new TemporalWorker(
            _env.Client,
            new TemporalWorkerOptions($"test-postexec-{Guid.NewGuid():N}")
                .AddAllActivities(stubs)
                .AddAllActivities(new StageActivityStubs())
                .AddWorkflow<PostExecutionPipelineWorkflow>());

        await worker.ExecuteAsync(async () =>
        {
            var input = new PostExecInput(
                SessionId: "pe-happy",
                ContainerId: "cid-1",
                WorkingDirectory: "/workspace",
                AgentResponse: "agent finished",
                AiAssistant: "claude");

            var handle = await _env.Client.StartWorkflowAsync(
                (PostExecutionPipelineWorkflow wf) => wf.RunAsync(input),
                new(id: $"pe-happy-{Guid.NewGuid():N}",
                    taskQueue: worker.Options.TaskQueue!));

            var result = await handle.GetResultAsync();

            result.ReportGenerated.Should().BeTrue();
            result.ReportMarkdown.Should().Be("# Session Report\nAll good.");
            result.CostUsd.Should().Be(0.05m);

            stubs.VerifyCallCount.Should().Be(1);
            stubs.RunCliAgentCallCount.Should().Be(1);

            await ReplayFixtureHelper.CaptureIfMissingAsync(
                handle, "post-execution-pipeline", "happy-path-v1.json");
        });
    }

    /// <summary>
    /// Stub bag for the two activities the workflow invokes.
    /// Activity-name rule: method name minus "Async" suffix, so these register
    /// as "RunGates" and "RunCliAgent".
    /// </summary>
    public class PostExecStubs
    {
        public Func<VerifyInput, VerifyOutput> VerifyResponder { get; set; } =
            _ => new VerifyOutput(
                AllPassed: true,
                FailedGates: Array.Empty<string>(),
                GateResultsJson: "[]");

        public Func<RunCliAgentInput, RunCliAgentOutput> RunResponder { get; set; } =
            _ => new RunCliAgentOutput(
                Response: "# Session Report\nAll good.",
                StructuredOutputJson: null,
                Success: true,
                CostUsd: 0.05m,
                InputTokens: 10,
                OutputTokens: 20,
                FilesModified: Array.Empty<string>(),
                ExitCode: 0,
                AssistantSessionId: "stub");

        public int VerifyCallCount { get; private set; }
        public int RunCliAgentCallCount { get; private set; }

        [Activity]
        public Task<VerifyOutput> RunGatesAsync(VerifyInput i)
        {
            VerifyCallCount++;
            return Task.FromResult(VerifyResponder(i));
        }

        [Activity]
        public Task<RunCliAgentOutput> RunCliAgentAsync(RunCliAgentInput i)
        {
            RunCliAgentCallCount++;
            return Task.FromResult(RunResponder(i));
        }
    }
}
