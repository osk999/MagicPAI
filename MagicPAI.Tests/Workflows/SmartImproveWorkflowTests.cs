using FluentAssertions;
using Temporalio.Activities;
using Temporalio.Testing;
using Temporalio.Worker;
using MagicPAI.Activities.Contracts;
using MagicPAI.Server.Workflows;
using MagicPAI.Workflows.Contracts;

namespace MagicPAI.Tests.Workflows;

/// <summary>
/// Workflow unit tests for <see cref="SmartImproveWorkflow"/> — the top-level
/// burst/verify oscillator. Pure helpers have direct unit tests. Workflow
/// behaviour is verified end-to-end with WorkflowEnvironment + activity
/// stubs, including the dual-clean-verify termination and budget guard.
/// See newplan.md §7.2 (test plan).
/// </summary>
[Trait("Category", "Integration")]
public class SmartImproveWorkflowTests : IAsyncLifetime
{
    private WorkflowEnvironment _env = null!;

    public async Task InitializeAsync() =>
        _env = await WorkflowEnvironment.StartTimeSkippingAsync();

    public async Task DisposeAsync()
    {
        if (_env is not null) await _env.ShutdownAsync();
    }

    // ── Pure helper tests ──────────────────────────────────────────────

    [Fact]
    public void ResolveBurstSchedule_DefaultIs8855Steady()
    {
        var input = BuildInput() with { MaxBursts = 5 };
        var sched = SmartImproveWorkflow.ResolveBurstSchedule(input);

        sched.Should().Equal(new[] { 8, 8, 5, 5, 5 });
    }

    [Fact]
    public void ResolveBurstSchedule_HonorsCallerOverride()
    {
        var input = BuildInput() with { BurstSchedule = new[] { 3, 2, 1 } };
        var sched = SmartImproveWorkflow.ResolveBurstSchedule(input);

        sched.Should().Equal(new[] { 3, 2, 1 });
    }

    [Fact]
    public void ResolveBurstSchedule_HonorsSteadyStateSize()
    {
        var input = BuildInput() with { MaxBursts = 4, SteadyStateBurstSize = 3 };
        var sched = SmartImproveWorkflow.ResolveBurstSchedule(input);

        sched.Should().Equal(new[] { 8, 8, 3, 3 });
    }

    [Fact]
    public void BuildBurstPrompt_FirstBurst_UsesOriginalPrompt()
    {
        var prompt = SmartImproveWorkflow.BuildBurstPrompt(
            "Build a todo API", "[]", completedBursts: 0);

        prompt.Should().Contain("Build a todo API");
        prompt.Should().Contain("SmartImprove autonomous loop");
    }

    [Fact]
    public void BuildBurstPrompt_SubsequentBurst_FocusesOnFailures()
    {
        var prompt = SmartImproveWorkflow.BuildBurstPrompt(
            "Build a todo API",
            "[{\"rubricItemId\":\"build\",\"priority\":\"P0\"}]",
            completedBursts: 1);

        prompt.Should().Contain("ORIGINAL TASK");
        prompt.Should().Contain("FAILURES");
        prompt.Should().Contain("PRIORITY-FIRST");
    }

    [Fact]
    public void MergeFailureLists_DeduplicatesById()
    {
        var r1 = new[]
        {
            new RubricFailure("build", "P0", "real", "ev1"),
            new RubricFailure("test", "P0", "real", "ev2"),
        };
        var r2 = new[]
        {
            new RubricFailure("build", "P0", "real", "ev1-r2"),  // same id
            new RubricFailure("lint", "P2", "real", "ev3"),
        };

        var merged = SmartImproveWorkflow.MergeFailureLists(r1, r2);

        // Should contain build, test, lint — three unique ids.
        merged.Should().Contain("build")
              .And.Contain("test")
              .And.Contain("lint");
    }

    [Fact]
    public void SnapshotFrom_CountsRealFailuresByPriority()
    {
        const string rubric = """
            { "items": [ { "id":"a" }, { "id":"b" }, { "id":"c" }, { "id":"d" } ] }
            """;
        const string classified = """
            [
              { "rubricItemId":"a","priority":"P0","classification":"real" },
              { "rubricItemId":"b","priority":"P1","classification":"real" },
              { "rubricItemId":"c","priority":"P1","classification":"environmental" },
              { "rubricItemId":"d","priority":"P2","classification":"real" }
            ]
            """;
        var dummy = new VerifyHarnessOutput(0, 0, 0, 0, 0, 0,
            Array.Empty<RubricFailure>(), "");

        var snap = SmartImproveWorkflow.SnapshotFrom(rubric, classified, dummy, dummy);

        snap.TotalItems.Should().Be(4);
        snap.FailedP0.Should().Be(1);
        snap.FailedP1.Should().Be(1);  // environmental excluded
        snap.FailedP2.Should().Be(1);
        snap.FailedP3.Should().Be(0);
        snap.PassedItems.Should().Be(1); // 4 total - 3 real failures
    }

    [Fact]
    public void ExtractRemainingP2P3_ReturnsOnlyRealLowPriority()
    {
        const string classified = """
            [
              { "rubricItemId":"crit","priority":"P0","classification":"real" },
              { "rubricItemId":"polish","priority":"P2","classification":"real" },
              { "rubricItemId":"flaky","priority":"P2","classification":"environmental" },
              { "rubricItemId":"nit","priority":"P3","classification":"real" }
            ]
            """;

        var remaining = SmartImproveWorkflow.ExtractRemainingP2P3(classified);

        remaining.Should().BeEquivalentTo(new[] { "polish", "nit" });
    }

    // ── Workflow tests with stubs ──────────────────────────────────────

    [Fact]
    public async Task VerifierCleanTwiceInARow_ExitsVerifiedClean()
    {
        var stubs = new Stubs();
        // Verifier always returns clean. First burst's two verifies are
        // both clean → stableStreak = 1. Second burst's two are also clean
        // → stableStreak = 2 → EXIT.
        // (Default RequiredCleanVerifies = 2.)
        stubs.VerifyResponder = _ =>
            new VerifyHarnessOutput(0, 0, 0, 0, 0, 0,
                Array.Empty<RubricFailure>(), "empty");

        var input = BuildInput() with { MaxBursts = 5, RequiredCleanVerifies = 2 };
        var result = await RunWorkflow(stubs, input);

        result.ExitReason.Should().Be("verified-clean");
        result.BurstsCompleted.Should().Be(2);  // burstIndex incremented twice
    }

    [Fact]
    public async Task EmptyRubric_ExitsImmediately()
    {
        var stubs = new Stubs();
        // Generate-rubric returns 0 items.
        stubs.RubricResponder = _ => new GenerateRubricOutput(
            ProjectType: "unknown",
            Rationale: "could not analyze",
            RubricJson: "{\"items\":[]}",
            RubricItemCount: 0,
            CostUsd: 0.01m);

        var result = await RunWorkflow(stubs, BuildInput());

        result.ExitReason.Should().Be("no-rubric-items");
        result.BurstsCompleted.Should().Be(0);
    }

    [Fact]
    public async Task VerifierAlwaysDirty_HitsMaxBursts()
    {
        var stubs = new Stubs();
        stubs.VerifyResponder = _ => new VerifyHarnessOutput(
            RealP0Count: 1, RealP1Count: 0, RealP2Count: 0, RealP3Count: 0,
            StructuralCount: 0, EnvironmentalCount: 0,
            Failures: new[]
            {
                new RubricFailure("build", "P0", "real", "fail"),
            },
            FailureSetHash: "h1");

        // Tiny cap so the test finishes fast.
        var input = BuildInput() with { MaxBursts = 2, MaxTotalIterations = 1000 };
        var result = await RunWorkflow(stubs, input);

        result.ExitReason.Should().Be("max-bursts");
        result.BurstsCompleted.Should().Be(2);
    }

    // ── Reward-hack canaries — the most important guarantees of the design ──

    [Fact]
    public async Task ModelClaimsDoneButVerifierDisagrees_NeverExitsVerifiedClean()
    {
        // Reward-hack scenario: every burst's child workflow exits
        // 'silence-confirmed' (the model emits [DONE] AND its filesystem is
        // empty for the silence countdown). But the EXTERNAL verifier
        // continues to report a real P0 failure.
        //
        // Per newplan.md §4 the workflow MUST refuse to terminate as
        // 'verified-clean' in this case — the verifier is ground truth,
        // not the model's self-report. Exit reason should be max-bursts.
        var stubs = new Stubs();

        // RunCliAgent always emits [DONE] on its own line so the burst's
        // silence-countdown completes cheaply each iteration.
        stubs.RunResponder = _ => new RunCliAgentOutput(
            Response: "all good\n[DONE]",
            StructuredOutputJson: null, Success: true,
            CostUsd: 0.001m, InputTokens: 1, OutputTokens: 1,
            FilesModified: Array.Empty<string>(), ExitCode: 0,
            AssistantSessionId: "stub");
        // Filesystem is stable (empty deltas) — silence-countdown will
        // confirm — but the verifier ALWAYS reports a real P0 failure.
        stubs.FsResponder = _ => new SnapshotFilesystemOutput(
            new Dictionary<string, string> { ["src/foo.cs"] = "stable-h" },
            1, 1, false);
        stubs.VerifyResponder = _ => new VerifyHarnessOutput(
            RealP0Count: 1, RealP1Count: 0, RealP2Count: 0, RealP3Count: 0,
            StructuralCount: 0, EnvironmentalCount: 0,
            Failures: new[] { new RubricFailure("build", "P0", "real", "still broken") },
            FailureSetHash: "h-stable");
        // Classifier confirms the failure as 'real' (real LLM-judge behaviour
        // for an unambiguous build error). Without this stub the default
        // classifier returns empty and FinalRubric.FailedP0 stays 0 — which
        // would still PASS the primary canary (ExitReason != verified-clean)
        // but mask the rubric snapshot for the second assertion below.
        stubs.ClassifyResponder = inp => new ClassifyFailuresOutput(
            ClassifiedFailuresJson: """
                [{"rubricItemId":"build","priority":"P0","classification":"real","evidence":"still broken"}]
                """,
            RealCount: 1, StructuralCount: 0, EnvironmentalCount: 0,
            CostUsd: 0.001m);

        var input = BuildInput() with
        {
            MaxBursts = 3,
            RequiredCleanVerifies = 2
        };
        var result = await RunWorkflow(stubs, input);

        // The workflow must NOT exit verified-clean — the verifier said no.
        result.ExitReason.Should().NotBe("verified-clean",
            because: "model self-report (silence-confirmed in each burst) " +
                     "must NOT override external verifier failures — this is " +
                     "the core anti-reward-hacking invariant of newplan.md §4");
        result.ExitReason.Should().Be("max-bursts");
        result.FinalRubric.FailedP0.Should().BeGreaterThan(0,
            because: "verifier reported a real P0; final snapshot should reflect it");
    }

    [Fact]
    public async Task TotalBudgetExceeded_ExitsBudgetWithoutStartingNextBurst()
    {
        var stubs = new Stubs();
        // Verifier always reports a real P0 so the loop wants to keep going.
        stubs.VerifyResponder = _ => new VerifyHarnessOutput(
            RealP0Count: 1, RealP1Count: 0, RealP2Count: 0, RealP3Count: 0,
            StructuralCount: 0, EnvironmentalCount: 0,
            Failures: new[] { new RubricFailure("x", "P0", "real", "still bad") },
            FailureSetHash: "h");
        // Each AI call costs $0.30. Budget cap = $0.50 → after the first
        // burst (which costs ~stub * 1 iter ≈ $0.01) plus cumulative cost,
        // budget exits before the second burst can start.
        stubs.RunResponder = _ => new RunCliAgentOutput(
            "did stuff", null, true, 0.30m, 1, 1,
            Array.Empty<string>(), 0, "stub");

        var input = BuildInput() with
        {
            MaxBursts = 5,
            MaxTotalBudgetUsd = 0.50m,
            BurstSchedule = new[] { 1, 1, 1 },
        };
        var result = await RunWorkflow(stubs, input);

        result.ExitReason.Should().Be("budget");
        result.TotalCostUsd.Should().BeGreaterOrEqualTo(0.50m);
    }

    [Fact]
    public async Task EmptyRubricGenerated_ExitsImmediatelyWithNoRubricItems()
    {
        // Already covered by EmptyRubric_ExitsImmediately, but explicitly
        // verify the exit-reason string is the documented sentinel
        // ("no-rubric-items") so RUNBOOK.md remains accurate.
        var stubs = new Stubs();
        stubs.RubricResponder = _ => new GenerateRubricOutput(
            ProjectType: "unknown", Rationale: "ambiguous prompt",
            RubricJson: "{\"items\":[]}", RubricItemCount: 0,
            CostUsd: 0.05m);

        var result = await RunWorkflow(stubs, BuildInput());

        result.ExitReason.Should().Be("no-rubric-items");
        result.FinalRubric.TotalItems.Should().Be(0);
        result.IterationsRun.Should().Be(0,
            because: "no bursts should run when the rubric is empty — there is " +
                     "literally nothing to verify against");
        result.BurstsCompleted.Should().Be(0);
    }

    [Fact]
    public async Task SingleBurstCleanVerify_WaitsForRequiredStreak()
    {
        // RequiredCleanVerifies=2 (default): a single clean cycle must NOT
        // terminate. The workflow must run at least 2 burst+verify cycles.
        var stubs = new Stubs();
        var verifyCalls = 0;
        stubs.VerifyResponder = _ =>
        {
            verifyCalls++;
            // Both runs of cycle 1 are clean.
            // Both runs of cycle 2 are also clean → terminates.
            return new VerifyHarnessOutput(0, 0, 0, 0, 0, 0,
                Array.Empty<RubricFailure>(), "");
        };

        var input = BuildInput() with { RequiredCleanVerifies = 2, MaxBursts = 5 };
        var result = await RunWorkflow(stubs, input);

        result.ExitReason.Should().Be("verified-clean");
        // Cycle 1: 2 verifies (run-1, run-2). Cycle 2: 2 more verifies.
        // Verifies happen TWICE per burst (separated dual-clean). So 2 bursts × 2 = 4 calls.
        verifyCalls.Should().Be(4,
            because: "2 bursts × 2 separated verifier runs each");
    }

    [Fact]
    public async Task ValidationRejectsEmptySessionId()
    {
        var stubs = new Stubs();
        var input = BuildInput() with { SessionId = "" };
        var act = async () => await RunWorkflow(stubs, input);

        await act.Should().ThrowAsync<Exception>();
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static SmartImproveInput BuildInput() => new(
        SessionId: "smart-improve-test",
        Prompt: "Improve the project end-to-end.",
        AiAssistant: "claude",
        WorkspacePath: "/workspace",
        MaxTotalIterations: 100,
        MaxTotalBudgetUsd: 0m,
        MaxBursts: 5,
        RequiredCleanVerifies: 2);

    private async Task<SmartImproveOutput> RunWorkflow(
        Stubs stubs, SmartImproveInput input)
    {
        // Need to register both SmartImproveWorkflow + the children it
        // dispatches: ContextGathererWorkflow + SmartIterativeLoopWorkflow.
        using var worker = new TemporalWorker(
            _env.Client,
            new TemporalWorkerOptions($"test-si-{Guid.NewGuid():N}")
                .AddAllActivities(stubs)
                .AddWorkflow<SmartImproveWorkflow>()
                .AddWorkflow<ContextGathererWorkflow>()
                .AddWorkflow<SmartIterativeLoopWorkflow>());

        return await worker.ExecuteAsync(async () =>
        {
            var handle = await _env.Client.StartWorkflowAsync(
                (SmartImproveWorkflow w) => w.RunAsync(input),
                new(id: $"si-{Guid.NewGuid():N}", taskQueue: worker.Options.TaskQueue!));
            return await handle.GetResultAsync();
        });
    }

    /// <summary>
    /// Stubs every activity SmartImproveWorkflow + its children call. The
    /// child workflows (ContextGatherer, SmartIterativeLoop) themselves are
    /// not stubbed — they run for real but talk to these stubs for I/O.
    /// </summary>
    public class Stubs
    {
        public Func<SpawnContainerInput, SpawnContainerOutput> SpawnResponder { get; set; } =
            i => new SpawnContainerOutput($"stub-{i.SessionId}", null);

        public Func<RunCliAgentInput, RunCliAgentOutput> RunResponder { get; set; } =
            _ => new RunCliAgentOutput(
                Response: "stub work\n[DONE]",
                StructuredOutputJson: null, Success: true,
                CostUsd: 0.01m, InputTokens: 1, OutputTokens: 1,
                FilesModified: Array.Empty<string>(), ExitCode: 0,
                AssistantSessionId: "stub-sess");

        public Func<GenerateRubricInput, GenerateRubricOutput> RubricResponder { get; set; } =
            _ => new GenerateRubricOutput(
                ProjectType: "library",
                Rationale: "tiny lib",
                RubricJson: """
                    { "items": [
                      { "id":"build","description":"compiles","priority":"P0",
                        "verificationCommand":"dotnet build","passCriteria":"exit-zero","isTrusted":true }
                    ] }
                    """,
                RubricItemCount: 1,
                CostUsd: 0.05m);

        public Func<PlanVerificationHarnessInput, PlanVerificationHarnessOutput> HarnessResponder { get; set; } =
            _ => new PlanVerificationHarnessOutput(
                HarnessScriptPath: ".smartimprove/harness.sh",
                CommandsByRubricId: new Dictionary<string, string> { ["build"] = "dotnet build" },
                CostUsd: 0.05m);

        public Func<VerifyHarnessInput, VerifyHarnessOutput> VerifyResponder { get; set; } =
            _ => new VerifyHarnessOutput(0, 0, 0, 0, 0, 0,
                Array.Empty<RubricFailure>(), "");

        public Func<ClassifyFailuresInput, ClassifyFailuresOutput> ClassifyResponder { get; set; } =
            _ => new ClassifyFailuresOutput("[]", 0, 0, 0, 0m);

        public Func<SnapshotFilesystemInput, SnapshotFilesystemOutput> FsResponder { get; set; } =
            _ => new SnapshotFilesystemOutput(
                new Dictionary<string, string>(), 1, 0, false);

        public Func<ComputeAstHashInput, ComputeAstHashOutput> AstResponder { get; set; } =
            _ => new ComputeAstHashOutput("", 0, true);

        public Func<GetGitStateInput, GetGitStateOutput> GitResponder { get; set; } =
            _ => new GetGitStateOutput("", 0, true, true);

        public Func<ResearchPromptInput, ResearchPromptOutput> ResearchResponder { get; set; } =
            _ => new ResearchPromptOutput(
                EnhancedPrompt: "stub-research",
                CodebaseAnalysis: "stub codebase",
                ResearchContext: "stub context",
                Rationale: "stub");

        // Activity attributes — Temporal's hosted-worker scans for these.
        [Activity] public Task<SpawnContainerOutput> SpawnAsync(SpawnContainerInput i)
            => Task.FromResult(SpawnResponder(i));
        [Activity] public Task DestroyAsync(DestroyInput i) => Task.CompletedTask;
        [Activity] public Task<RunCliAgentOutput> RunCliAgentAsync(RunCliAgentInput i)
            => Task.FromResult(RunResponder(i));
        [Activity] public Task<GenerateRubricOutput> GenerateRubricAsync(GenerateRubricInput i)
            => Task.FromResult(RubricResponder(i));
        [Activity] public Task<PlanVerificationHarnessOutput> PlanVerificationHarnessAsync(PlanVerificationHarnessInput i)
            => Task.FromResult(HarnessResponder(i));
        [Activity] public Task<VerifyHarnessOutput> VerifyHarnessAsync(VerifyHarnessInput i)
            => Task.FromResult(VerifyResponder(i));
        [Activity] public Task<ClassifyFailuresOutput> ClassifyFailuresAsync(ClassifyFailuresInput i)
            => Task.FromResult(ClassifyResponder(i));
        [Activity] public Task<SnapshotFilesystemOutput> SnapshotFilesystemAsync(SnapshotFilesystemInput i)
            => Task.FromResult(FsResponder(i));
        [Activity] public Task<ComputeAstHashOutput> ComputeAstHashAsync(ComputeAstHashInput i)
            => Task.FromResult(AstResponder(i));
        [Activity] public Task<GetGitStateOutput> GetGitStateAsync(GetGitStateInput i)
            => Task.FromResult(GitResponder(i));
        [Activity] public Task<ResearchPromptOutput> ResearchPromptAsync(ResearchPromptInput i)
            => Task.FromResult(ResearchResponder(i));
    }
}
