using FluentAssertions;
using Temporalio.Activities;
using Temporalio.Testing;
using Temporalio.Worker;
using MagicPAI.Activities.Contracts;
using MagicPAI.Server.Workflows;
using MagicPAI.Workflows.Contracts;

namespace MagicPAI.Tests.Workflows;

/// <summary>
/// Integration tests for the Temporal <see cref="ResearchPipelineWorkflow"/>.
/// After the refactor the pipeline delegates the research pass to
/// <see cref="IterativeLoopWorkflow"/>, so the tests register both workflows
/// and stub the RunCliAgent/Spawn/Destroy activities the inner loop drives.
/// See temporal.md §H.8.
/// </summary>
[Trait("Category", "Integration")]
public class ResearchPipelineWorkflowTests : IAsyncLifetime
{
    private WorkflowEnvironment _env = null!;

    public async Task InitializeAsync() =>
        _env = await WorkflowEnvironment.StartTimeSkippingAsync();

    public async Task DisposeAsync()
    {
        if (_env is not null) await _env.ShutdownAsync();
    }

    // ── Section-parser unit tests (pure, no workflow env) ──────────────

    [Fact]
    public void ExtractSection_PullsH2_IgnoringCodaH3()
    {
        var response = $"""
            ## Rewritten Task
            refactor the auth layer with fully grounded scope

            ## Codebase Analysis
            AuthService, TokenStore

            ## Research Context
            external docs checked

            ## Rationale
            grounded

            ### Task Status
            - [x] everything done

            ### Completion
            Completion: true

            [DONE]
            """;

        ResearchPipelineWorkflow.ExtractSection(response, "Rewritten Task")
            .Should().Contain("refactor the auth layer");
        ResearchPipelineWorkflow.ExtractSection(response, "Research Context")
            .Should().Contain("external docs checked");
        ResearchPipelineWorkflow.ExtractSection(response, "Nonexistent")
            .Should().BeNull();
    }

    // ── Workflow behaviour ─────────────────────────────────────────────

    [Fact]
    public async Task ReusesCallerContainer_DispatchesLoop_ProjectsSections()
    {
        var stubs = new Stubs();
        stubs.RunResponder = _ => new RunCliAgentOutput(
            Response: BuildFullResearchResponse(),
            StructuredOutputJson: null, Success: true,
            CostUsd: 0.07m, InputTokens: 1, OutputTokens: 1,
            FilesModified: Array.Empty<string>(), ExitCode: 0,
            AssistantSessionId: "s");

        using var worker = new TemporalWorker(
            _env.Client,
            new TemporalWorkerOptions($"test-rp-{Guid.NewGuid():N}")
                .AddAllActivities(stubs)
                .AddWorkflow<ResearchPipelineWorkflow>()
                .AddWorkflow<IterativeLoopWorkflow>());

        await worker.ExecuteAsync(async () =>
        {
            var input = new ResearchPipelineInput(
                SessionId: "rp-reuse",
                Prompt: "refactor the auth layer",
                ContainerId: "cid-parent",
                WorkingDirectory: "/workspace",
                AiAssistant: "claude");

            var handle = await _env.Client.StartWorkflowAsync(
                (ResearchPipelineWorkflow wf) => wf.RunAsync(input),
                new(id: $"rp-reuse-{Guid.NewGuid():N}",
                    taskQueue: worker.Options.TaskQueue!));

            var result = await handle.GetResultAsync();

            result.ResearchedPrompt.Should().Contain("grounded rewrite");
            result.ResearchContext.Should().Contain("external docs checked");
            // Loop runs MinIterations (3) times; stub charges 0.07 per call.
            result.CostUsd.Should().Be(0.07m * 3);

            // Caller's container must be reused — no spawn, no destroy.
            stubs.SpawnCalls.Should().Be(0);
            stubs.DestroyCalls.Should().Be(0);
            stubs.LastRunContainerId.Should().Be("cid-parent");

            // Research runs use ModelPower=1 (strongest).
            stubs.LastRunModelPower.Should().Be(1);

            await ReplayFixtureHelper.CaptureIfMissingAsync(
                handle, "research-pipeline", "happy-path-v1.json");
        });
    }

    [Fact]
    public async Task SpawnsOwnContainer_WhenContainerIdMissing()
    {
        var stubs = new Stubs();
        stubs.RunResponder = _ => new RunCliAgentOutput(
            Response: BuildFullResearchResponse(),
            StructuredOutputJson: null, Success: true,
            CostUsd: 0.05m, InputTokens: 1, OutputTokens: 1,
            FilesModified: Array.Empty<string>(), ExitCode: 0,
            AssistantSessionId: null);

        using var worker = new TemporalWorker(
            _env.Client,
            new TemporalWorkerOptions($"test-rp-own-{Guid.NewGuid():N}")
                .AddAllActivities(stubs)
                .AddWorkflow<ResearchPipelineWorkflow>()
                .AddWorkflow<IterativeLoopWorkflow>());

        await worker.ExecuteAsync(async () =>
        {
            var input = new ResearchPipelineInput(
                SessionId: "rp-own",
                Prompt: "document the api surface",
                ContainerId: "",               // <— empty
                WorkingDirectory: "/workspace",
                AiAssistant: "claude");

            var handle = await _env.Client.StartWorkflowAsync(
                (ResearchPipelineWorkflow wf) => wf.RunAsync(input),
                new(id: $"rp-own-{Guid.NewGuid():N}",
                    taskQueue: worker.Options.TaskQueue!));

            await handle.GetResultAsync();

            stubs.SpawnCalls.Should().Be(1);
            stubs.DestroyCalls.Should().Be(1);
        });
    }

    [Fact]
    public async Task ReturnsEmptyResearchedPrompt_WhenRewrittenSectionMissing()
    {
        var stubs = new Stubs();
        // No `## Rewritten Task` section — valid coda so the loop exits, but
        // the pipeline must surface an EMPTY ResearchedPrompt so the upstream
        // Fix #143 guard can fall back to the caller's original prompt.
        stubs.RunResponder = _ => new RunCliAgentOutput(
            Response: """
                Some freeform analysis with no H2 sections.

                ### Task Status
                - [x] survey
                - [x] rewrite
                - [x] verify

                ### Completion
                Completion: true

                [DONE]
                """,
            StructuredOutputJson: null, Success: true,
            CostUsd: 0.03m, InputTokens: 1, OutputTokens: 1,
            FilesModified: Array.Empty<string>(), ExitCode: 0,
            AssistantSessionId: null);

        using var worker = new TemporalWorker(
            _env.Client,
            new TemporalWorkerOptions($"test-rp-fb-{Guid.NewGuid():N}")
                .AddAllActivities(stubs)
                .AddWorkflow<ResearchPipelineWorkflow>()
                .AddWorkflow<IterativeLoopWorkflow>());

        await worker.ExecuteAsync(async () =>
        {
            var input = new ResearchPipelineInput(
                SessionId: "rp-fb",
                Prompt: "X",
                ContainerId: "cid-parent",
                WorkingDirectory: "/workspace",
                AiAssistant: "claude");

            var handle = await _env.Client.StartWorkflowAsync(
                (ResearchPipelineWorkflow wf) => wf.RunAsync(input),
                new(id: $"rp-fb-{Guid.NewGuid():N}",
                    taskQueue: worker.Options.TaskQueue!));

            var result = await handle.GetResultAsync();
            result.ResearchedPrompt.Should().BeEmpty();
        });
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private static string BuildFullResearchResponse() => """
        ## Rewritten Task
        Produce a fully grounded rewrite of the user's request.

        ## Codebase Analysis
        Inspected AuthService, TokenStore, IdentityProvider.

        ## Research Context
        Relevant external docs checked: RFC 6749.

        ## Rationale
        Rewrite is grounded — files inspected directly match the scope.

        ### Task Status
        - [x] A1 Survey files
        - [x] A2 Scope
        - [x] A3 Open questions
        - [x] B1 Propose ≥3 candidates
        - [x] B2 Recommend one
        - [x] B3 Cite ≥5 refs
        - [x] C1 Milestone plan
        - [x] C2 Failure modes
        - [x] C3 Verification strategy
        - [x] C4 Risk list
        - [x] D1 Four H2 sections
        - [x] D2 Write research.md

        ### Current Work
        Completed the research pass.

        ### Blockers
        None

        ### Completion
        Completion: true

        [DONE]
        """;

    public class Stubs
    {
        public int SpawnCalls, DestroyCalls;
        public string? LastRunContainerId;
        public int LastRunModelPower;

        public Func<SpawnContainerInput, SpawnContainerOutput> SpawnResponder { get; set; } =
            i => new SpawnContainerOutput($"stub-{i.SessionId}", null);

        public Func<RunCliAgentInput, RunCliAgentOutput> RunResponder { get; set; } =
            _ => new RunCliAgentOutput("", null, true, 0m, 0, 0,
                Array.Empty<string>(), 0, null);

        [Activity]
        public Task<SpawnContainerOutput> SpawnAsync(SpawnContainerInput i)
        {
            Interlocked.Increment(ref SpawnCalls);
            return Task.FromResult(SpawnResponder(i));
        }

        [Activity]
        public Task DestroyAsync(DestroyInput i)
        {
            Interlocked.Increment(ref DestroyCalls);
            return Task.CompletedTask;
        }

        [Activity]
        public Task<RunCliAgentOutput> RunCliAgentAsync(RunCliAgentInput i)
        {
            LastRunContainerId = i.ContainerId;
            LastRunModelPower = i.ModelPower;
            return Task.FromResult(RunResponder(i));
        }

        // The pipeline falls back to `cat /workspace/research.md` when the
        // loop's FinalResponse lacks the H2 sections. Default stub returns an
        // empty file (exit 1) so callers that don't care get the fall-through
        // null behaviour; tests that do care can override ExecResponder.
        public Func<ExecInput, ExecOutput> ExecResponder { get; set; } =
            _ => new ExecOutput(ExitCode: 1, Output: "", Error: "no such file");

        [Activity]
        public Task<ExecOutput> ExecAsync(ExecInput i) =>
            Task.FromResult(ExecResponder(i));
    }
}
