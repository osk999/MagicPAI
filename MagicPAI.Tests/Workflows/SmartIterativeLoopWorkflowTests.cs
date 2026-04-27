using FluentAssertions;
using Temporalio.Activities;
using Temporalio.Testing;
using Temporalio.Worker;
using MagicPAI.Activities.Contracts;
using MagicPAI.Server.Workflows;
using MagicPAI.Workflows.Contracts;

namespace MagicPAI.Tests.Workflows;

/// <summary>
/// Workflow unit tests for <see cref="SmartIterativeLoopWorkflow"/> — the
/// per-burst smart-termination workflow. Uses Temporal's
/// <c>WorkflowEnvironment.StartTimeSkippingAsync</c> + activity stubs to
/// verify every branch of the termination state machine, including the
/// reward-hack canary (model emits [DONE] but keeps writing files →
/// silence countdown must reset).
///
/// See newplan.md §7.2 (test plan) and §4 (anti-reward-hacking).
/// </summary>
[Trait("Category", "Integration")]
public class SmartIterativeLoopWorkflowTests : IAsyncLifetime
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
    public void ContainsMarker_RecognizesMarkerOnOwnLine()
    {
        SmartIterativeLoopWorkflow.ContainsMarker("foo\n[DONE]\nbar", "[DONE]")
            .Should().BeTrue();
    }

    [Fact]
    public void ContainsMarker_RejectsMarkerInProse()
    {
        SmartIterativeLoopWorkflow.ContainsMarker("I will emit [DONE] soon.", "[DONE]")
            .Should().BeFalse();
    }

    [Fact]
    public void ContainsMarker_NullOrEmpty_IsFalse()
    {
        SmartIterativeLoopWorkflow.ContainsMarker(null, "[DONE]").Should().BeFalse();
        SmartIterativeLoopWorkflow.ContainsMarker("", "[DONE]").Should().BeFalse();
        SmartIterativeLoopWorkflow.ContainsMarker("foo", "").Should().BeFalse();
    }

    // ── Termination — silence countdown happy path ─────────────────────

    [Fact]
    public async Task EmitsDoneThenSilent_ExitsSilenceConfirmed()
    {
        var stubs = new Stubs();
        // Iteration 1 — does work, emits [DONE]
        // Iteration 2 — silence pass: filesystem unchanged
        // Iteration 3 — silence pass: filesystem unchanged → exit
        var responses = new Queue<string>(new[]
        {
            "I built the thing.\n[DONE]",
            "Nothing left to do.",
            "Confirmed nothing left.",
        });
        stubs.RunResponder = _ => StubRunOutput(responses.Dequeue());
        // Filesystem doesn't change after iteration 1.
        stubs.FsResponder = _ => new SnapshotFilesystemOutput(
            FileHashes: new Dictionary<string, string> { ["src/foo.cs"] = "hash-1" },
            CapturedAtUnixSeconds: 1, FileCount: 1, TruncatedByMaxFiles: false);

        var result = await RunLoop(stubs, BuildInput(maxIter: 10, minIter: 1));

        result.ExitReason.Should().Be("silence-confirmed");
        result.SilenceConfirmed.Should().BeTrue();
        result.DoneSignalled.Should().BeTrue();
        result.IterationsRun.Should().Be(3);
    }

    // ── Termination — reward-hack canary ───────────────────────────────

    [Fact]
    public async Task EmitsDoneButKeepsWriting_SilenceCountResets()
    {
        var stubs = new Stubs();
        var responses = new Queue<string>(new[]
        {
            // Marker must be on its OWN line per ContainsMarker — narrative
            // mentions like "I'll emit [DONE] later" should NOT detect.
            "Did some work.\n[DONE]",          // iter 1: emit done
            "Still touching things.",          // iter 2: fs CHANGED → silence resets
            "Now genuinely done.",             // iter 3: fs unchanged → silence count = 1
            "Truly final.",                    // iter 4: fs unchanged → silence count = 2 → exit
            "fallback-5",                      // safety: surface workflow bugs cleanly
            "fallback-6",
        });
        stubs.RunResponder = _ =>
        {
            var resp = responses.Count == 0 ? "queue-empty" : responses.Dequeue();
            return StubRunOutput(resp);
        };

        // Iteration 1 → fs A. Iteration 2 → fs B (changed!). Iteration 3 → fs B
        // (unchanged). Iteration 4 → fs B (unchanged). The silence countdown
        // must reset on iter 2 and only complete after iter 3+4.
        var fsAttempt = 0;
        stubs.FsResponder = _ =>
        {
            fsAttempt++;
            // The captures are: baseline, post-iter1, post-iter2, post-iter3, post-iter4
            return fsAttempt switch
            {
                1 => Snap("hash-baseline"),  // baseline
                2 => Snap("hash-A"),          // post-iter1
                3 => Snap("hash-B"),          // post-iter2 (CHANGED!)
                _ => Snap("hash-B"),          // post-iter3, 4 (stable)
            };
        };

        var result = await RunLoop(stubs, BuildInput(maxIter: 10, minIter: 1));

        result.ExitReason.Should().Be("silence-confirmed");
        result.IterationsRun.Should().Be(4);
        result.DoneSignalled.Should().BeTrue();
    }

    // ── Tests/ tripwire ────────────────────────────────────────────────

    [Fact]
    public async Task ModelTouchesTestsFolder_TripwireFlagSet()
    {
        var stubs = new Stubs();
        stubs.RunResponder = _ => StubRunOutput("did stuff");

        // Baseline: nothing in tests/. Post-iter: a file under tests/ appears.
        var fsAttempt = 0;
        stubs.FsResponder = _ =>
        {
            fsAttempt++;
            // baseline, then a tests file appears
            return fsAttempt == 1
                ? new SnapshotFilesystemOutput(
                    new Dictionary<string, string> { ["src/foo.cs"] = "h1" },
                    1, 1, false)
                : new SnapshotFilesystemOutput(
                    new Dictionary<string, string>
                    {
                        ["src/foo.cs"] = "h1",
                        ["tests/foo_test.cs"] = "h2",
                    },
                    2, 2, false);
        };

        var result = await RunLoop(stubs, BuildInput(maxIter: 1, minIter: 1));

        result.TestsTripped.Should().BeTrue();
    }

    // ── No-progress (git) ──────────────────────────────────────────────

    [Fact]
    public async Task GitHeadAndAstStable_ExitsNoProgress()
    {
        var stubs = new Stubs();
        stubs.RunResponder = _ => StubRunOutput("doing nothing");
        // Git: same HEAD, clean every iteration.
        stubs.GitResponder = _ => new GetGitStateOutput("sha-A", 0, true, false);
        // AST: same hash every iteration.
        stubs.AstResponder = _ => new ComputeAstHashOutput("ast-hash-A", 1, false);
        // Filesystem can vary — no-progress only requires git+AST agreement.
        stubs.FsResponder = _ => Snap("fs-hash");

        var result = await RunLoop(stubs, BuildInput(maxIter: 10, minIter: 1, noProgressThresh: 3));

        result.ExitReason.Should().Be("no-progress");
        // Iteration 1 establishes baseline-vs-post comparison; counter starts
        // incrementing from there. Three matching iterations → exit.
        result.IterationsRun.Should().Be(3);
    }

    [Fact]
    public async Task AstChanges_NoProgressDoesNotFire()
    {
        var stubs = new Stubs();
        stubs.RunResponder = _ => StubRunOutput("changing things");
        stubs.GitResponder = _ => new GetGitStateOutput("sha-A", 0, true, false);  // git unchanged
        // AST changes every iteration → only 1 of 2 signals fires; default
        // requires 2 of 2 → no-progress counter never increments.
        var astAttempt = 0;
        stubs.AstResponder = _ =>
        {
            astAttempt++;
            return new ComputeAstHashOutput($"ast-hash-{astAttempt}", 1, false);
        };
        stubs.FsResponder = _ => Snap("fs-hash");

        var result = await RunLoop(stubs, BuildInput(maxIter: 4, minIter: 1));

        // Should hit max-iterations rather than no-progress because AST
        // signal disagreed each iteration.
        result.ExitReason.Should().Be("max-iterations");
    }

    // ── Question guard ─────────────────────────────────────────────────

    [Fact]
    public async Task QuestionInResponse_NextPromptForcesAutonomousMode()
    {
        var stubs = new Stubs();
        var promptsSeen = new List<string>();
        var responses = new Queue<string>(new[]
        {
            "Should I use option A or B?",   // iter 1 — asks a question
            "Going with A.\n[DONE]",          // iter 2 — must NOT ask
            "All good.",                      // iter 3 — silence
            "Still good.",                    // iter 4 — silence → exit
        });
        stubs.RunResponder = inp =>
        {
            promptsSeen.Add(inp.Prompt);
            return StubRunOutput(responses.Dequeue());
        };
        stubs.FsResponder = _ => Snap("fs-stable");

        await RunLoop(stubs, BuildInput(maxIter: 6, minIter: 1));

        // Iteration 2's prompt must reference autonomous mode.
        promptsSeen.Should().HaveCountGreaterThan(1);
        promptsSeen[1].Should().Contain("headless automation",
            because: "the question guard should mutate the next prompt");
    }

    // ── Budget guard ───────────────────────────────────────────────────

    [Fact]
    public async Task ExceedsBudget_ExitsBudget()
    {
        var stubs = new Stubs();
        // Each iteration costs $5. Budget cap = $10 → exit after iter 2.
        stubs.RunResponder = _ => StubRunOutput("working", costUsd: 5m);
        stubs.FsResponder = _ => Snap("fs-stable");

        var input = BuildInput(maxIter: 10, minIter: 1) with { MaxBudgetUsd = 10m };
        var result = await RunLoop(stubs, input);

        result.ExitReason.Should().Be("budget");
        result.TotalCostUsd.Should().BeGreaterOrEqualTo(10m);
    }

    // ── Stop signal ────────────────────────────────────────────────────

    [Fact]
    public async Task StopSignal_ExitsBetweenIterations()
    {
        var stubs = new Stubs();
        var iter = 0;
        stubs.RunResponder = inp =>
        {
            iter++;
            return StubRunOutput($"iter {iter}");
        };
        stubs.FsResponder = _ => Snap("fs-stable");

        using var worker = new TemporalWorker(
            _env.Client,
            new TemporalWorkerOptions($"test-sil-{Guid.NewGuid():N}")
                .AddAllActivities(stubs)
                .AddWorkflow<SmartIterativeLoopWorkflow>());

        var result = await worker.ExecuteAsync(async () =>
        {
            var input = BuildInput(maxIter: 100, minIter: 1);
            var handle = await _env.Client.StartWorkflowAsync(
                (SmartIterativeLoopWorkflow w) => w.RunAsync(input),
                new(id: $"sil-{Guid.NewGuid():N}", taskQueue: worker.Options.TaskQueue!));

            // Let it run a bit, then signal stop.
            await Task.Delay(100);
            await handle.SignalAsync(w => w.RequestStopAsync("test-stop"));

            return await handle.GetResultAsync();
        });

        result.ExitReason.Should().Be("signal");
    }

    // ── Validation ─────────────────────────────────────────────────────

    [Fact]
    public async Task EmptyContainerId_Throws()
    {
        var stubs = new Stubs();
        var input = BuildInput(maxIter: 5, minIter: 1) with { ContainerId = "" };
        var act = async () => await RunLoop(stubs, input);

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task MinExceedsMax_Throws()
    {
        var stubs = new Stubs();
        var input = BuildInput(maxIter: 2, minIter: 5);
        var act = async () => await RunLoop(stubs, input);

        await act.Should().ThrowAsync<Exception>();
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private static SmartIterativeLoopInput BuildInput(
        int maxIter = 5, int minIter = 1, int noProgressThresh = 3) =>
        new(
            SessionId: "s-1",
            ContainerId: "ctr-1",
            WorkspacePath: "/workspace",
            Prompt: "Improve the thing.",
            AiAssistant: "claude",
            Model: null,
            ModelPower: 2,
            MaxIterations: maxIter,
            MinIterations: minIter,
            NoProgressThreshold: noProgressThresh);

    private static SnapshotFilesystemOutput Snap(string hash) =>
        new(
            FileHashes: new Dictionary<string, string> { ["src/foo.cs"] = hash },
            CapturedAtUnixSeconds: 1,
            FileCount: 1,
            TruncatedByMaxFiles: false);

    private static RunCliAgentOutput StubRunOutput(string response, decimal costUsd = 0.01m) =>
        new(
            Response: response,
            StructuredOutputJson: null,
            Success: true,
            CostUsd: costUsd,
            InputTokens: 1,
            OutputTokens: 1,
            FilesModified: Array.Empty<string>(),
            ExitCode: 0,
            AssistantSessionId: "stub-sess");

    private async Task<SmartIterativeLoopOutput> RunLoop(Stubs stubs, SmartIterativeLoopInput input)
    {
        using var worker = new TemporalWorker(
            _env.Client,
            new TemporalWorkerOptions($"test-sil-{Guid.NewGuid():N}")
                .AddAllActivities(stubs)
                .AddWorkflow<SmartIterativeLoopWorkflow>());

        return await worker.ExecuteAsync(async () =>
        {
            var handle = await _env.Client.StartWorkflowAsync(
                (SmartIterativeLoopWorkflow w) => w.RunAsync(input),
                new(id: $"sil-{Guid.NewGuid():N}", taskQueue: worker.Options.TaskQueue!));
            return await handle.GetResultAsync();
        });
    }

    /// <summary>
    /// Stubs for the four activities the workflow calls. Each responder is a
    /// hook tests use to control behavior per iteration.
    /// </summary>
    public class Stubs
    {
        public Func<RunCliAgentInput, RunCliAgentOutput> RunResponder { get; set; } =
            _ => new RunCliAgentOutput("", null, true, 0m, 0, 0, Array.Empty<string>(), 0, null);

        public Func<SnapshotFilesystemInput, SnapshotFilesystemOutput> FsResponder { get; set; } =
            _ => new SnapshotFilesystemOutput(
                new Dictionary<string, string>(), 1, 0, false);

        public Func<ComputeAstHashInput, ComputeAstHashOutput> AstResponder { get; set; } =
            _ => new ComputeAstHashOutput("", 0, true);

        public Func<GetGitStateInput, GetGitStateOutput> GitResponder { get; set; } =
            _ => new GetGitStateOutput("", 0, true, true);

        [Activity]
        public Task<RunCliAgentOutput> RunCliAgentAsync(RunCliAgentInput i)
            => Task.FromResult(RunResponder(i));

        [Activity]
        public Task<SnapshotFilesystemOutput> SnapshotFilesystemAsync(SnapshotFilesystemInput i)
            => Task.FromResult(FsResponder(i));

        [Activity]
        public Task<ComputeAstHashOutput> ComputeAstHashAsync(ComputeAstHashInput i)
            => Task.FromResult(AstResponder(i));

        [Activity]
        public Task<GetGitStateOutput> GetGitStateAsync(GetGitStateInput i)
            => Task.FromResult(GitResponder(i));
    }
}
