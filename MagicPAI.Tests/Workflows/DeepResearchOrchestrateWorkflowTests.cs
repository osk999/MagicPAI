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
/// Registers the full child chain —
/// <see cref="ResearchPipelineWorkflow"/> (now iterative) →
/// <see cref="IterativeLoopWorkflow"/> (research loop) →
/// <see cref="StandardOrchestrateWorkflow"/> → <see cref="VerifyAndRepairWorkflow"/> —
/// so the whole cascade runs against stubbed activities.
/// See temporal.md §H.13.
/// </summary>
[Trait("Category", "Integration")]
public class DeepResearchOrchestrateWorkflowTests : IAsyncLifetime
{
    private WorkflowEnvironment _env = null!;

    public async Task InitializeAsync() =>
        _env = await WorkflowEnvironment.StartTimeSkippingAsync();

    public async Task DisposeAsync()
    {
        if (_env is not null) await _env.ShutdownAsync();
    }

    /// <summary>
    /// Fix #143 regression guard: when the research pipeline's output contains
    /// no salvageable text, DeepResearchOrchestrate must fall back to the
    /// original user prompt when dispatching StandardOrchestrate — NOT pass an
    /// empty string that breaks the Claude CLI.
    /// </summary>
    [Fact]
    public async Task Fix143_EmptyResearchResult_FallsBackToInputPrompt()
    {
        var stubs = new DeepResearchStubs();

        // First RunCliAgent call = research loop (return an empty response +
        // valid done coda so the loop exits immediately with no content).
        // Second RunCliAgent call = StandardOrchestrate's main run.
        var runIndex = 0;
        string? observedOrchestrateRunPrompt = null;
        stubs.RunResponder = i =>
        {
            runIndex++;
            if (runIndex == 1)
            {
                // Empty FinalResponse after trim — fallback should kick in.
                return new RunCliAgentOutput(
                    Response: "\n### Task Status\n- [x] done\n\n### Completion\nCompletion: true\n\n[DONE]",
                    StructuredOutputJson: null, Success: true,
                    CostUsd: 0.01m, InputTokens: 1, OutputTokens: 1,
                    FilesModified: Array.Empty<string>(), ExitCode: 0,
                    AssistantSessionId: "stub");
            }

            // StandardOrchestrate main run.
            observedOrchestrateRunPrompt = i.Prompt;
            return new RunCliAgentOutput(
                Response: "ok", StructuredOutputJson: null, Success: true,
                CostUsd: 0.02m, InputTokens: 1, OutputTokens: 1,
                FilesModified: Array.Empty<string>(), ExitCode: 0,
                AssistantSessionId: "stub");
        };

        using var worker = new TemporalWorker(
            _env.Client,
            new TemporalWorkerOptions($"test-dro-empty-{Guid.NewGuid():N}")
                .AddAllActivities(stubs)
                .AddAllActivities(new StageActivityStubs())
                .AddWorkflow<DeepResearchOrchestrateWorkflow>()
                .AddWorkflow<ResearchPipelineWorkflow>()
                .AddWorkflow<IterativeLoopWorkflow>()
                .AddWorkflow<StandardOrchestrateWorkflow>()
                .AddWorkflow<VerifyAndRepairWorkflow>());

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

            await handle.GetResultAsync();

            // StandardOrchestrate received the original prompt (Fix #143).
            observedOrchestrateRunPrompt.Should().NotBeNullOrEmpty();
            observedOrchestrateRunPrompt.Should().Contain("ORIGINAL_PROMPT");
        });
    }

    [Fact]
    public async Task Chains_ResearchLoop_ThenStandardOrchestrate()
    {
        var stubs = new DeepResearchStubs();

        // Differentiate the research-loop runs from the final StandardOrchestrate
        // run by prompt-content sniffing. The loop prompt contains the research
        // scaffold; the implementation run gets the rewritten prompt.
        var researchResponse = """
            ## Rewritten Task
            Refactored & grounded scope for the caching layer.

            ## Codebase Analysis
            ICacheStore, IMemoryProvider identified.

            ## Research Context
            Found AuthService, TokenStore.

            ## Rationale
            All files inspected.

            ### Task Status
            - [x] A1 Survey
            - [x] A2 Scope
            - [x] A3 Questions
            - [x] B1 Candidates
            - [x] B2 Recommend
            - [x] B3 Refs
            - [x] C1 Milestones
            - [x] C2 Failures
            - [x] C3 Verification
            - [x] C4 Risks
            - [x] D1 Sections
            - [x] D2 research.md

            ### Completion
            Completion: true

            [DONE]
            """;

        stubs.RunResponder = i =>
        {
            // Distinguish research-loop iterations (first N calls) from the
            // StandardOrchestrate main implementation run (which uses the
            // "enhanced:" prompt produced by EnhancePromptAsync).
            var isOrchestrateRun = i.Prompt.StartsWith("enhanced:");
            if (!isOrchestrateRun)
            {
                return new RunCliAgentOutput(
                    Response: researchResponse,
                    StructuredOutputJson: null, Success: true,
                    CostUsd: 0.05m, InputTokens: 1, OutputTokens: 1,
                    FilesModified: Array.Empty<string>(), ExitCode: 0,
                    AssistantSessionId: "s-research");
            }
            return new RunCliAgentOutput(
                Response: "stub-run-output",
                StructuredOutputJson: null, Success: true,
                CostUsd: 0.25m, InputTokens: 50, OutputTokens: 80,
                FilesModified: Array.Empty<string>(), ExitCode: 0,
                AssistantSessionId: "s-impl");
        };

        using var worker = new TemporalWorker(
            _env.Client,
            new TemporalWorkerOptions($"test-dro-{Guid.NewGuid():N}")
                .AddAllActivities(stubs)
                .AddAllActivities(new StageActivityStubs())
                .AddWorkflow<DeepResearchOrchestrateWorkflow>()
                .AddWorkflow<ResearchPipelineWorkflow>()
                .AddWorkflow<IterativeLoopWorkflow>()
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

            // StandardOrchestrate's main run response surfaces here.
            result.Response.Should().Be("stub-run-output");
            result.VerificationPassed.Should().BeTrue();
            // ResearchContext comes from the parsed ## Research Context H2.
            result.ResearchSummary.Should().Contain("Found AuthService, TokenStore");
            result.TotalCostUsd.Should().BeGreaterThan(0m);

            // Two containers spawned: parent (DeepResearchOrchestrate) + child
            // StandardOrchestrate. ResearchPipeline reuses parent's container.
            stubs.SpawnCallCount.Should().Be(2);
            stubs.DestroyedContainerIds.Should().HaveCount(2);

            // RunCliAgent fires MinIterations (3) times inside the research
            // loop + once for StandardOrchestrate's main run.
            stubs.RunCliAgentCallCount.Should().Be(4);
            stubs.EnhanceCallCount.Should().Be(1);  // StandardOrchestrate step
            stubs.VerifyCallCount.Should().Be(1);

            await ReplayFixtureHelper.CaptureIfMissingAsync(
                handle, "deep-research-orchestrate", "happy-path-v1.json");
        });
    }

    public class DeepResearchStubs
    {
        public Func<SpawnContainerInput, SpawnContainerOutput> SpawnResponder { get; set; } =
            i => new SpawnContainerOutput($"stub-container-{i.SessionId}", null);

        public Func<EnhancePromptInput, EnhancePromptOutput> EnhanceResponder { get; set; } =
            i => new EnhancePromptOutput(
                EnhancedPrompt: $"enhanced: {i.OriginalPrompt}",
                WasEnhanced: true,
                Rationale: "stub");

        public Func<RunCliAgentInput, RunCliAgentOutput> RunResponder { get; set; } =
            _ => new RunCliAgentOutput(
                Response: "stub-run-output",
                StructuredOutputJson: null, Success: true,
                CostUsd: 0.25m, InputTokens: 50, OutputTokens: 80,
                FilesModified: Array.Empty<string>(), ExitCode: 0,
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
        private int _enhanceCalls;
        private int _runCliAgentCalls;
        private int _verifyCalls;

        public int SpawnCallCount => _spawnCalls;
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

        // ResearchPipeline falls back to `cat /workspace/research.md` when the
        // loop's FinalResponse has no H2 sections. Default stub: no file.
        public Func<ExecInput, ExecOutput> ExecResponder { get; set; } =
            _ => new ExecOutput(ExitCode: 1, Output: "", Error: "no such file");

        [Activity]
        public Task<ExecOutput> ExecAsync(ExecInput i) =>
            Task.FromResult(ExecResponder(i));

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
