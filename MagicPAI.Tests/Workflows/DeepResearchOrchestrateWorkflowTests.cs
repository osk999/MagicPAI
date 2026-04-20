using FluentAssertions;
using Temporalio.Activities;
using Temporalio.Testing;
using Temporalio.Worker;
using MagicPAI.Activities.Contracts;
using MagicPAI.Server.Workflows;
using MagicPAI.Workflows.Contracts;

namespace MagicPAI.Tests.Workflows;

/// <summary>
/// Integration tests for the Temporal <see cref="DeepResearchOrchestrateWorkflow"/>.
/// Registers the two child workflows (<see cref="ResearchPipelineWorkflow"/>
/// and <see cref="StandardOrchestrateWorkflow"/>) with the VerifyAndRepair
/// grandchild so the whole chain executes against stubbed activities.
/// See temporal.md §H.13.
/// </summary>
[Trait("Category", "Integration")]
public class DeepResearchOrchestrateWorkflowTests : IAsyncLifetime
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
    /// Fix #143 regression guard: when ResearchPipeline returns an empty
    /// <see cref="ResearchPromptOutput.EnhancedPrompt"/> (legitimate when
    /// no research is actually warranted), DeepResearchOrchestrate must
    /// fall back to the original user prompt when dispatching
    /// StandardOrchestrate — NOT pass an empty string that breaks the
    /// Claude CLI.
    /// </summary>
    [Fact]
    public async Task Fix143_EmptyResearchPrompt_FallsBackToInputPrompt()
    {
        var stubs = new DeepResearchStubs
        {
            ResearchResponder = i => new ResearchPromptOutput(
                EnhancedPrompt: "",                  // <— empty
                CodebaseAnalysis: "",
                ResearchContext: "no research needed",
                Rationale: "trivial"),
        };

        using var worker = new TemporalWorker(
            _env.Client,
            new TemporalWorkerOptions($"test-dro-empty-{Guid.NewGuid():N}")
                .AddAllActivities(stubs)
                .AddWorkflow<DeepResearchOrchestrateWorkflow>()
                .AddWorkflow<ResearchPipelineWorkflow>()
                .AddWorkflow<StandardOrchestrateWorkflow>()
                .AddWorkflow<VerifyAndRepairWorkflow>());

        // Capture the prompt that StandardOrchestrate receives — it should
        // be the original input prompt, not the empty string.
        string? observedRunPrompt = null;
        stubs.RunResponder = i =>
        {
            observedRunPrompt = i.Prompt;
            return new RunCliAgentOutput(
                Response: "ok", StructuredOutputJson: null, Success: true,
                CostUsd: 0.01m, InputTokens: 1, OutputTokens: 1,
                FilesModified: Array.Empty<string>(), ExitCode: 0,
                AssistantSessionId: "stub");
        };

        await worker.ExecuteAsync(async () =>
        {
            var input = new DeepResearchOrchestrateInput(
                SessionId: "dro-empty",
                Prompt: "ORIGINAL_PROMPT",
                WorkspacePath: "/workspace",
                AiAssistant: "claude",
                Model: null,
                ModelPower: 1,
                EnableGui: false);

            var handle = await _env.Client.StartWorkflowAsync(
                (DeepResearchOrchestrateWorkflow wf) => wf.RunAsync(input),
                new(id: $"dro-empty-{Guid.NewGuid():N}",
                    taskQueue: worker.Options.TaskQueue!));

            var result = await handle.GetResultAsync();
            result.Should().NotBeNull();

            // The crucial assertion — StandardOrchestrate received the
            // original prompt, not empty string, because of Fix #143.
            observedRunPrompt.Should().NotBeNullOrEmpty();
            observedRunPrompt.Should().Contain("ORIGINAL_PROMPT");
        });
    }

    [Fact]
    public async Task Chains_ResearchPipeline_ThenStandardOrchestrate()
    {
        var stubs = new DeepResearchStubs();

        using var worker = new TemporalWorker(
            _env.Client,
            new TemporalWorkerOptions($"test-dro-{Guid.NewGuid():N}")
                .AddAllActivities(stubs)
                .AddWorkflow<DeepResearchOrchestrateWorkflow>()
                .AddWorkflow<ResearchPipelineWorkflow>()
                .AddWorkflow<StandardOrchestrateWorkflow>()
                .AddWorkflow<VerifyAndRepairWorkflow>());

        await worker.ExecuteAsync(async () =>
        {
            var input = new DeepResearchOrchestrateInput(
                SessionId: "dro-happy",
                Prompt: "design a caching layer",
                WorkspacePath: "/workspace",
                AiAssistant: "claude",
                Model: null,
                ModelPower: 1,
                EnableGui: false);

            var handle = await _env.Client.StartWorkflowAsync(
                (DeepResearchOrchestrateWorkflow wf) => wf.RunAsync(input),
                new(id: $"dro-happy-{Guid.NewGuid():N}",
                    taskQueue: worker.Options.TaskQueue!));

            var result = await handle.GetResultAsync();

            // StandardOrchestrate's implementation run response surfaces here.
            result.Response.Should().Be("stub-run-output");
            result.VerificationPassed.Should().BeTrue();
            // ResearchContext string from the pipeline child.
            result.ResearchSummary.Should().Be("Found AuthService, TokenStore.");
            result.TotalCostUsd.Should().BeGreaterThan(0m);

            // Two containers spawned: parent (this workflow) + child
            // StandardOrchestrate. Both are destroyed.
            stubs.SpawnCallCount.Should().Be(2);
            stubs.DestroyedContainerIds.Should().HaveCount(2);

            stubs.ResearchCallCount.Should().Be(1);
            stubs.EnhanceCallCount.Should().Be(1);  // StandardOrchestrate step
            stubs.RunCliAgentCallCount.Should().Be(1);  // StandardOrchestrate main run
            stubs.VerifyCallCount.Should().Be(1);   // VerifyAndRepair first attempt passes

            await ReplayFixtureHelper.CaptureIfMissingAsync(
                handle, "deep-research-orchestrate", "happy-path-v1.json");
        });
    }

    public class DeepResearchStubs
    {
        public Func<SpawnContainerInput, SpawnContainerOutput> SpawnResponder { get; set; } =
            i => new SpawnContainerOutput($"stub-container-{i.SessionId}", null);

        public Func<ResearchPromptInput, ResearchPromptOutput> ResearchResponder { get; set; } =
            i => new ResearchPromptOutput(
                EnhancedPrompt: $"researched: {i.Prompt}",
                CodebaseAnalysis: "AuthService, TokenStore",
                ResearchContext: "Found AuthService, TokenStore.",
                Rationale: "grounded");

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

        private int _spawnCalls;
        private int _researchCalls;
        private int _enhanceCalls;
        private int _runCliAgentCalls;
        private int _verifyCalls;

        public int SpawnCallCount => _spawnCalls;
        public int ResearchCallCount => _researchCalls;
        public int EnhanceCallCount => _enhanceCalls;
        public int RunCliAgentCallCount => _runCliAgentCalls;
        public int VerifyCallCount => _verifyCalls;

        [Activity]
        public Task<SpawnContainerOutput> SpawnAsync(SpawnContainerInput i)
        {
            Interlocked.Increment(ref _spawnCalls);
            return Task.FromResult(SpawnResponder(i));
        }

        [Activity]
        public Task DestroyAsync(DestroyInput i)
        {
            lock (DestroyedContainerIds) { DestroyedContainerIds.Add(i.ContainerId); }
            return Task.CompletedTask;
        }

        [Activity]
        public Task<ResearchPromptOutput> ResearchPromptAsync(ResearchPromptInput i)
        {
            Interlocked.Increment(ref _researchCalls);
            return Task.FromResult(ResearchResponder(i));
        }

        [Activity]
        public Task<EnhancePromptOutput> EnhancePromptAsync(EnhancePromptInput i)
        {
            Interlocked.Increment(ref _enhanceCalls);
            return Task.FromResult(EnhanceResponder(i));
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
            Interlocked.Increment(ref _verifyCalls);
            return Task.FromResult(VerifyResponder(i));
        }

        [Activity]
        public Task<RepairOutput> GenerateRepairPromptAsync(RepairInput i) =>
            Task.FromResult(RepairResponder(i));
    }
}
