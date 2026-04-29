using FluentAssertions;
using Temporalio.Activities;
using Temporalio.Testing;
using Temporalio.Worker;
using MagicPAI.Activities.Contracts;
using MagicPAI.Server.Workflows;
using MagicPAI.Workflows.Contracts;

namespace MagicPAI.Tests.Workflows;

/// <summary>
/// Integration tests for the Temporal <see cref="StandardOrchestrateWorkflow"/>.
/// Registers both the orchestrator and the <see cref="VerifyAndRepairWorkflow"/>
/// child on the test worker and stubs the shared activity surface:
/// Spawn / Destroy / EnhancePrompt / RunCliAgent / RunGates / GenerateRepairPrompt.
/// See temporal.md §H.9.
/// </summary>
[Trait("Category", "Integration")]
public class StandardOrchestrateWorkflowTests : IAsyncLifetime
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
    /// Happy path — EnhancePrompt + RunCliAgent + Verify passes on first try,
    /// no repair. Verifies container spawn/destroy lifecycle.
    /// </summary>
    [Fact]
    public async Task Completes_HappyPath_VerificationPasses()
    {
        var stubs = new StandardOrchestrateStubs();

        using var worker = new TemporalWorker(
            _env.Client,
            new TemporalWorkerOptions($"test-std-orch-{Guid.NewGuid():N}")
                .AddAllActivities(stubs)
                .AddAllActivities(new StageActivityStubs())
                .AddWorkflow<StandardOrchestrateWorkflow>()
                .AddWorkflow<VerifyAndRepairWorkflow>());

        await worker.ExecuteAsync(async () =>
        {
            var input = new StandardOrchestrateInput(
                SessionId: "so-happy",
                Prompt: "implement feature",
                WorkspacePath: "/workspace",
                AiAssistant: "claude",
                Model: null,
                ModelPower: 2,
                EnableGui: false);

            var handle = await _env.Client.StartWorkflowAsync(
                (StandardOrchestrateWorkflow wf) => wf.RunAsync(input),
                new(id: $"so-happy-{Guid.NewGuid():N}",
                    taskQueue: worker.Options.TaskQueue!));

            var result = await handle.GetResultAsync();

            result.Response.Should().Be("stub-run-output");
            result.VerificationPassed.Should().BeTrue();
            // Main cost: one RunCliAgent invocation ($0.25). Repair cost: 0
            // (first verify passes so no rerun).
            result.TotalCostUsd.Should().Be(0.25m);

            stubs.SpawnCallCount.Should().Be(1);
            stubs.DestroyedContainerIds.Should().ContainSingle()
                .Which.Should().Be("stub-container");
            stubs.EnhanceCallCount.Should().Be(1);
            stubs.RunCliAgentCallCount.Should().Be(1);   // main run; no repair
            stubs.VerifyCallCount.Should().Be(1);
            stubs.RepairPromptCallCount.Should().Be(0);

            await ReplayFixtureHelper.CaptureIfMissingAsync(
                handle, "standard-orchestrate", "happy-path-v1.json");
        });
    }

    /// <summary>
    /// Stub bag spanning every activity the orchestrator + verify-repair child
    /// touch. Counts each invocation for assertions.
    /// </summary>
    public class StandardOrchestrateStubs
    {
        public Func<SpawnContainerInput, SpawnContainerOutput> SpawnResponder { get; set; } =
            _ => new SpawnContainerOutput("stub-container", null);

        public Func<EnhancePromptInput, EnhancePromptOutput> EnhanceResponder { get; set; } =
            i => new EnhancePromptOutput(
                EnhancedPrompt: $"enhanced: {i.OriginalPrompt}",
                WasEnhanced: true,
                Rationale: "stub");

        public Func<RunCliAgentInput, RunCliAgentOutput> RunResponder { get; set; } =
            _ => new RunCliAgentOutput(
                Response: "stub-run-output",
                StructuredOutputJson: null,
                Success: true,
                CostUsd: 0.25m,
                InputTokens: 50,
                OutputTokens: 80,
                FilesModified: Array.Empty<string>(),
                ExitCode: 0,
                AssistantSessionId: "stub");

        public Func<VerifyInput, VerifyOutput> VerifyResponder { get; set; } =
            _ => new VerifyOutput(
                AllPassed: true,
                FailedGates: Array.Empty<string>(),
                GateResultsJson: "[]");

        public Func<RepairInput, RepairOutput> RepairResponder { get; set; } =
            _ => new RepairOutput(RepairPrompt: "fix", ShouldAttemptRepair: true);

        public List<string> DestroyedContainerIds { get; } = new();

        public int SpawnCallCount { get; private set; }
        public int EnhanceCallCount { get; private set; }
        public int RunCliAgentCallCount { get; private set; }
        public int VerifyCallCount { get; private set; }
        public int RepairPromptCallCount { get; private set; }

        [Activity]
        public Task<SpawnContainerOutput> SpawnAsync(SpawnContainerInput i)
        {
            SpawnCallCount++;
            return Task.FromResult(SpawnResponder(i));
        }

        [Activity]
        public Task DestroyAsync(DestroyInput i)
        {
            lock (DestroyedContainerIds) { DestroyedContainerIds.Add(i.ContainerId); }
            return Task.CompletedTask;
        }

        [Activity]
        public Task<EnhancePromptOutput> EnhancePromptAsync(EnhancePromptInput i)
        {
            EnhanceCallCount++;
            return Task.FromResult(EnhanceResponder(i));
        }

        [Activity]
        public Task<RunCliAgentOutput> RunCliAgentAsync(RunCliAgentInput i)
        {
            RunCliAgentCallCount++;
            return Task.FromResult(RunResponder(i));
        }

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
    }
}
