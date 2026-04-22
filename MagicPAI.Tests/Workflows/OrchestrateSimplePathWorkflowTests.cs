using FluentAssertions;
using Temporalio.Activities;
using Temporalio.Testing;
using Temporalio.Worker;
using MagicPAI.Activities.Contracts;
using MagicPAI.Server.Workflows;
using MagicPAI.Workflows.Contracts;

namespace MagicPAI.Tests.Workflows;

/// <summary>
/// Integration tests for the Temporal <see cref="OrchestrateSimplePathWorkflow"/>.
/// The orchestrator dispatches <see cref="SimpleAgentWorkflow"/> as a child and
/// maps its output. We register both workflows on the same test worker and stub
/// the activities SimpleAgent needs so the child runs to completion without
/// touching Docker/Claude.
/// </summary>
[Trait("Category", "Integration")]
public class OrchestrateSimplePathWorkflowTests : IAsyncLifetime
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
    /// Happy path — orchestrator dispatches SimpleAgent child, child runs
    /// Spawn/Run/Verify/Coverage/Destroy with all-happy stubs. Asserts the
    /// projected output matches the child's.
    /// </summary>
    [Fact]
    public async Task Completes_HappyPath_DelegatesToSimpleAgent()
    {
        var stubs = new SimpleAgentWorkflowTests.SimpleAgentStubs();

        using var worker = new TemporalWorker(
            _env.Client,
            new TemporalWorkerOptions($"test-orch-simple-{Guid.NewGuid():N}")
                .AddAllActivities(stubs)
                .AddWorkflow<OrchestrateSimplePathWorkflow>()
                .AddWorkflow<SimpleAgentWorkflow>());

        await worker.ExecuteAsync(async () =>
        {
            // Orchestrator already owns a container; it forwards the id to the
            // SimpleAgent child as ExistingContainerId so the child skips
            // Spawn + Destroy (the bug fix — double-spawn used to collide on
            // noVNC port 6080).
            var input = new OrchestrateSimpleInput(
                SessionId: "orch-simple-happy",
                Prompt: "hello world",
                ContainerId: "caller-owned-container",
                WorkspacePath: "/workspace",
                AiAssistant: "claude",
                Model: null,
                ModelPower: 2,
                EnableGui: false);

            var handle = await _env.Client.StartWorkflowAsync(
                (OrchestrateSimplePathWorkflow wf) => wf.RunAsync(input),
                new(id: $"orch-simple-happy-{Guid.NewGuid():N}",
                    taskQueue: worker.Options.TaskQueue!));

            var result = await handle.GetResultAsync();

            result.Response.Should().Be("stub-response");
            result.VerificationPassed.Should().BeTrue();
            result.TotalCostUsd.Should().Be(0.25m);

            // Child activities were driven through the stub bag exactly once each.
            stubs.RunCliAgentCallCount.Should().Be(1);
            stubs.VerifyCallCount.Should().Be(1);
            stubs.CoverageCallCount.Should().Be(1);
            // The caller supplied a container; SimpleAgent must NOT spawn a
            // second one, and must NOT destroy the caller's container.
            stubs.SpawnCallCount.Should().Be(0,
                because: "caller owns the container — SimpleAgent reuses it");
            stubs.DestroyedContainerIds.Should().BeEmpty(
                because: "the child must not tear down the caller's container");

            await ReplayFixtureHelper.CaptureIfMissingAsync(
                handle, "orchestrate-simple-path", "happy-path-v1.json");
        });
    }

    /// <summary>
    /// Top-level dispatch scenario — when the HTTP API hits this workflow
    /// directly, the incoming OrchestrateSimpleInput carries ContainerId="".
    /// The orchestrator must forward null as ExistingContainerId so the child
    /// SimpleAgent spawns + destroys its own container. This protects the
    /// standalone contract for the API entry point.
    /// </summary>
    [Fact]
    public async Task TopLevelDispatch_WithEmptyContainerId_AllowsChildToSpawn()
    {
        var stubs = new SimpleAgentWorkflowTests.SimpleAgentStubs();

        using var worker = new TemporalWorker(
            _env.Client,
            new TemporalWorkerOptions($"test-orch-simple-toplevel-{Guid.NewGuid():N}")
                .AddAllActivities(stubs)
                .AddWorkflow<OrchestrateSimplePathWorkflow>()
                .AddWorkflow<SimpleAgentWorkflow>());

        await worker.ExecuteAsync(async () =>
        {
            var input = new OrchestrateSimpleInput(
                SessionId: "orch-simple-toplevel",
                Prompt: "hello world",
                ContainerId: "",                       // top-level dispatch
                WorkspacePath: "/workspace",
                AiAssistant: "claude",
                Model: null,
                ModelPower: 2,
                EnableGui: false);

            var handle = await _env.Client.StartWorkflowAsync(
                (OrchestrateSimplePathWorkflow wf) => wf.RunAsync(input),
                new(id: $"orch-simple-toplevel-{Guid.NewGuid():N}",
                    taskQueue: worker.Options.TaskQueue!));

            var result = await handle.GetResultAsync();

            result.Response.Should().Be("stub-response");
            result.VerificationPassed.Should().BeTrue();

            // Child spawned + destroyed its own container because the caller
            // did not own one.
            stubs.SpawnCallCount.Should().Be(1);
            stubs.DestroyedContainerIds.Should().ContainSingle();
        });
    }
}
