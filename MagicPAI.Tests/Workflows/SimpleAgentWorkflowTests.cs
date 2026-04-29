using FluentAssertions;
using Temporalio.Activities;
using Temporalio.Client;
using Temporalio.Testing;
using Temporalio.Worker;
using MagicPAI.Activities.Contracts;
using MagicPAI.Server.Workflows;
using MagicPAI.Workflows.Contracts;

namespace MagicPAI.Tests.Workflows;

/// <summary>
/// Integration tests for the Temporal <see cref="SimpleAgentWorkflow"/>.
/// Uses <see cref="WorkflowEnvironment.StartTimeSkippingAsync"/> — a real
/// in-process Temporal dev server with time-skipping — plus stubbed activity
/// methods so the workflow runs fully without touching Docker/Claude.
/// See temporal.md §15.4 and RR.5 for the canonical pattern.
/// </summary>
[Trait("Category", "Integration")]
public class SimpleAgentWorkflowTests : IAsyncLifetime
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
    /// Happy path — Spawn → RunCliAgent → RunGates (pass) → GradeCoverage (AllMet)
    /// → Destroy. One agent run, one verify, one coverage call, no repair loop.
    /// Also captures a replay-baseline history to
    /// <c>Workflows/Histories/simple-agent/happy-path-v1.json</c>.
    /// </summary>
    [Fact]
    public async Task Completes_HappyPath_AndCapturesReplayFixture()
    {
        var stubs = new SimpleAgentStubs();

        using var worker = new TemporalWorker(
            _env.Client,
            new TemporalWorkerOptions($"test-simple-happy-{Guid.NewGuid():N}")
                .AddAllActivities(stubs)
                .AddAllActivities(new StageActivityStubs())
                .AddWorkflow<SimpleAgentWorkflow>());

        await worker.ExecuteAsync(async () =>
        {
            var input = new SimpleAgentInput(
                SessionId: "wf-happy",
                Prompt: "hello world",
                AiAssistant: "claude",
                Model: null,
                ModelPower: 2,
                WorkspacePath: "/workspace",
                EnableGui: false);

            var handle = await _env.Client.StartWorkflowAsync(
                (SimpleAgentWorkflow wf) => wf.RunAsync(input),
                new(id: $"wf-happy-{Guid.NewGuid():N}", taskQueue: worker.Options.TaskQueue!));

            var result = await handle.GetResultAsync();

            result.Response.Should().Be("stub-response");
            result.VerificationPassed.Should().BeTrue();
            // Coverage AllMet on first iteration → loop body breaks before
            // incrementing; the counter records 1 (the only iteration run).
            result.CoverageIterations.Should().Be(1);
            result.TotalCostUsd.Should().Be(0.25m);
            result.FilesModified.Should().ContainSingle().Which.Should().Be("foo.cs");
            stubs.DestroyedContainerIds.Should().ContainSingle()
                .Which.Should().Be("fake-container-1");
            stubs.VerifyCallCount.Should().Be(1);
            stubs.CoverageCallCount.Should().Be(1);
            stubs.RunCliAgentCallCount.Should().Be(1);

            await SaveReplayFixtureAsync(handle, "happy-path-v1.json");
        });
    }

    /// <summary>
    /// Coverage-loop path — first GradeCoverage returns AllMet=false, second
    /// returns AllMet=true. Expect two agent runs (original + repair), two
    /// verify passes, two coverage calls, and <c>CoverageIteration == 2</c>.
    /// Captures a second replay fixture at
    /// <c>Workflows/Histories/simple-agent/coverage-loop-v1.json</c>.
    /// </summary>
    [Fact]
    public async Task LoopsCoverage_UntilAllMet()
    {
        var coverageCalls = 0;
        var stubs = new SimpleAgentStubs
        {
            CoverageResponder = _ =>
            {
                coverageCalls++;
                return coverageCalls switch
                {
                    1 => new CoverageOutput(
                        AllMet: false,
                        GapPrompt: "add missing method",
                        CoverageReportJson: "{\"gaps\":[\"method-missing\"]}",
                        Iteration: 1),
                    _ => new CoverageOutput(
                        AllMet: true,
                        GapPrompt: "",
                        CoverageReportJson: "{\"gaps\":[]}",
                        Iteration: 2)
                };
            },
        };

        using var worker = new TemporalWorker(
            _env.Client,
            new TemporalWorkerOptions($"test-simple-cov-{Guid.NewGuid():N}")
                .AddAllActivities(stubs)
                .AddAllActivities(new StageActivityStubs())
                .AddWorkflow<SimpleAgentWorkflow>());

        await worker.ExecuteAsync(async () =>
        {
            var input = new SimpleAgentInput(
                SessionId: "wf-cov",
                Prompt: "implement thing",
                AiAssistant: "claude",
                Model: null,
                ModelPower: 2,
                WorkspacePath: "/workspace",
                EnableGui: false,
                MaxCoverageIterations: 3);

            var handle = await _env.Client.StartWorkflowAsync(
                (SimpleAgentWorkflow wf) => wf.RunAsync(input),
                new(id: $"wf-cov-{Guid.NewGuid():N}", taskQueue: worker.Options.TaskQueue!));

            var result = await handle.GetResultAsync();

            result.VerificationPassed.Should().BeTrue();
            result.CoverageIterations.Should().Be(2);
            result.TotalCostUsd.Should().Be(0.50m); // two runs at 0.25 each
            stubs.RunCliAgentCallCount.Should().Be(2);
            stubs.VerifyCallCount.Should().Be(2);
            stubs.CoverageCallCount.Should().Be(2);
            stubs.DestroyedContainerIds.Should().ContainSingle();

            await SaveReplayFixtureAsync(handle, "coverage-loop-v1.json");
        });
    }

    /// <summary>
    /// Child-mode contract: when <see cref="SimpleAgentInput.ExistingContainerId"/>
    /// is supplied, the workflow must NOT schedule Spawn or Destroy activities —
    /// the caller (an orchestrator) owns the container lifecycle. Verifies that
    /// every Run/Verify/Coverage activity receives the caller-supplied container
    /// id, proving the branch works end-to-end and guarding against the
    /// double-spawn port-6080 collision that motivated the bug fix.
    /// </summary>
    [Fact]
    public async Task UsesExistingContainer_WhenProvided()
    {
        var runCalls = new List<string>();
        var verifyCalls = new List<string>();
        var coverageCalls = new List<string>();
        var stubs = new SimpleAgentStubs
        {
            RunResponder = i =>
            {
                runCalls.Add(i.ContainerId);
                return new RunCliAgentOutput(
                    Response: "stub-response",
                    StructuredOutputJson: null,
                    Success: true,
                    CostUsd: 0.25m,
                    InputTokens: 100,
                    OutputTokens: 200,
                    FilesModified: new[] { "foo.cs" },
                    ExitCode: 0,
                    AssistantSessionId: "stub");
            },
            VerifyResponder = i =>
            {
                verifyCalls.Add(i.ContainerId);
                return new VerifyOutput(
                    AllPassed: true,
                    FailedGates: Array.Empty<string>(),
                    GateResultsJson: "[]");
            },
            CoverageResponder = i =>
            {
                coverageCalls.Add(i.ContainerId);
                return new CoverageOutput(
                    AllMet: true,
                    GapPrompt: "",
                    CoverageReportJson: "{\"gaps\":[]}",
                    Iteration: i.CurrentIteration);
            },
        };

        using var worker = new TemporalWorker(
            _env.Client,
            new TemporalWorkerOptions($"test-simple-existing-{Guid.NewGuid():N}")
                .AddAllActivities(stubs)
                .AddAllActivities(new StageActivityStubs())
                .AddWorkflow<SimpleAgentWorkflow>());

        await worker.ExecuteAsync(async () =>
        {
            const string parentContainerId = "parent-owned-container-42";

            var input = new SimpleAgentInput(
                SessionId: "wf-existing",
                Prompt: "hello from parent",
                AiAssistant: "claude",
                Model: null,
                ModelPower: 2,
                WorkspacePath: "/workspace",
                EnableGui: false,
                ExistingContainerId: parentContainerId);

            var handle = await _env.Client.StartWorkflowAsync(
                (SimpleAgentWorkflow wf) => wf.RunAsync(input),
                new(id: $"wf-existing-{Guid.NewGuid():N}",
                    taskQueue: worker.Options.TaskQueue!));

            var result = await handle.GetResultAsync();

            // Happy-path output still applies — coverage AllMet on first pass.
            result.Response.Should().Be("stub-response");
            result.VerificationPassed.Should().BeTrue();
            result.CoverageIterations.Should().Be(1);
            result.TotalCostUsd.Should().Be(0.25m);

            // The bug fix: no Spawn, no Destroy when a container is supplied.
            stubs.SpawnCallCount.Should().Be(0,
                because: "the caller already owns a container; double-spawn " +
                         "would collide on noVNC port 6080");
            stubs.DestroyedContainerIds.Should().BeEmpty(
                because: "the child must not tear down a container it does not own");

            // Every downstream activity must use the caller-supplied container id.
            runCalls.Should().ContainSingle().Which.Should().Be(parentContainerId);
            verifyCalls.Should().ContainSingle().Which.Should().Be(parentContainerId);
            coverageCalls.Should().ContainSingle().Which.Should().Be(parentContainerId);
        });
    }

    /// <summary>
    /// Finally-block contract: DestroyAsync must run even if RunCliAgentAsync throws.
    /// </summary>
    [Fact]
    public async Task DestroysContainer_EvenWhenRunCliAgentThrows()
    {
        var stubs = new SimpleAgentStubs
        {
            RunResponder = _ => throw new Temporalio.Exceptions.ApplicationFailureException(
                "boom", errorType: "TestFailure", nonRetryable: true)
        };

        using var worker = new TemporalWorker(
            _env.Client,
            new TemporalWorkerOptions($"test-simple-fail-{Guid.NewGuid():N}")
                .AddAllActivities(stubs)
                .AddAllActivities(new StageActivityStubs())
                .AddWorkflow<SimpleAgentWorkflow>());

        await worker.ExecuteAsync(async () =>
        {
            var input = new SimpleAgentInput(
                SessionId: "wf-fail",
                Prompt: "fail",
                AiAssistant: "claude",
                Model: null,
                ModelPower: 2,
                WorkspacePath: "/workspace",
                EnableGui: false);

            var handle = await _env.Client.StartWorkflowAsync(
                (SimpleAgentWorkflow wf) => wf.RunAsync(input),
                new(id: $"wf-fail-{Guid.NewGuid():N}", taskQueue: worker.Options.TaskQueue!));

            var act = async () => await handle.GetResultAsync();
            await act.Should().ThrowAsync<Temporalio.Exceptions.WorkflowFailedException>();

            stubs.DestroyedContainerIds.Should().ContainSingle()
                .Which.Should().Be("fake-container-1");
        });
    }

    /// <summary>
    /// Captures the workflow's history to a JSON file under Workflows/Histories/simple-agent/
    /// — both in the bin output (so the Replay test can find it) and back in
    /// the source tree (so the file is easy to commit).
    /// </summary>
    private static async Task SaveReplayFixtureAsync(
        WorkflowHandle<SimpleAgentWorkflow, SimpleAgentOutput> handle,
        string fileName)
    {
        var history = await handle.FetchHistoryAsync();
        var fixtureRel = Path.Combine("Workflows", "Histories", "simple-agent", fileName);
        var outDir = Path.Combine(AppContext.BaseDirectory, "Workflows", "Histories",
            "simple-agent");
        Directory.CreateDirectory(outDir);
        await File.WriteAllTextAsync(Path.Combine(AppContext.BaseDirectory, fixtureRel),
            history.ToJson());

        try
        {
            var binDir = new DirectoryInfo(AppContext.BaseDirectory);
            var projectDir = binDir.Parent?.Parent?.Parent;   // bin\Debug\net10.0
            if (projectDir is not null)
            {
                var srcFixtures = Path.Combine(projectDir.FullName, "Workflows",
                    "Histories", "simple-agent");
                Directory.CreateDirectory(srcFixtures);
                await File.WriteAllTextAsync(Path.Combine(srcFixtures, fileName),
                    history.ToJson());
            }
        }
        catch
        {
            // Best-effort mirroring. Test already asserted success.
        }
    }

    /// <summary>
    /// Stub activity bag registered with the <see cref="TemporalWorker"/>.
    /// Methods match the real activity signatures by name (<c>SpawnAsync</c>,
    /// <c>RunCliAgentAsync</c>, <c>DestroyAsync</c>, <c>RunGatesAsync</c>,
    /// <c>GradeCoverageAsync</c>) so Temporal routes by name.
    /// </summary>
    public class SimpleAgentStubs
    {
        public Func<SpawnContainerInput, SpawnContainerOutput> SpawnResponder { get; set; }
            = _ => new SpawnContainerOutput("fake-container-1", null);

        public Func<RunCliAgentInput, RunCliAgentOutput> RunResponder { get; set; }
            = _ => new RunCliAgentOutput(
                Response: "stub-response",
                StructuredOutputJson: null,
                Success: true,
                CostUsd: 0.25m,
                InputTokens: 100,
                OutputTokens: 200,
                FilesModified: new[] { "foo.cs" },
                ExitCode: 0,
                AssistantSessionId: "stub-session");

        public Func<VerifyInput, VerifyOutput> VerifyResponder { get; set; } =
            _ => new VerifyOutput(
                AllPassed: true,
                FailedGates: Array.Empty<string>(),
                GateResultsJson: "[]");

        public Func<CoverageInput, CoverageOutput> CoverageResponder { get; set; } =
            i => new CoverageOutput(
                AllMet: true,
                GapPrompt: "",
                CoverageReportJson: "{\"gaps\":[]}",
                Iteration: i.CurrentIteration);

        public List<string> DestroyedContainerIds { get; } = new();

        // Counters use Interlocked for safety — activities inside a single
        // workflow run are sequential so this is belt-and-braces, but keeps
        // the pattern uniform with the parallel-fan-out tests.
        private int _runCliAgentCalls;
        private int _verifyCalls;
        private int _coverageCalls;
        private int _spawnCalls;

        public int RunCliAgentCallCount => _runCliAgentCalls;
        public int VerifyCallCount => _verifyCalls;
        public int CoverageCallCount => _coverageCalls;
        public int SpawnCallCount => _spawnCalls;

        // Default [Activity] name strips "Async" off method names whose return
        // type is Task/Task<T>. So these register as "Spawn", "RunCliAgent",
        // "Destroy", "RunGates", "GradeCoverage" — matching the real activity
        // names referenced from the workflow via Workflow.ExecuteActivityAsync.
        [Activity]
        public Task<SpawnContainerOutput> SpawnAsync(SpawnContainerInput i)
        {
            Interlocked.Increment(ref _spawnCalls);
            return Task.FromResult(SpawnResponder(i));
        }

        [Activity]
        public Task<RunCliAgentOutput> RunCliAgentAsync(RunCliAgentInput i)
        {
            Interlocked.Increment(ref _runCliAgentCalls);
            return Task.FromResult(RunResponder(i));
        }

        [Activity]
        public Task DestroyAsync(DestroyInput i)
        {
            lock (DestroyedContainerIds) { DestroyedContainerIds.Add(i.ContainerId); }
            return Task.CompletedTask;
        }

        [Activity]
        public Task<VerifyOutput> RunGatesAsync(VerifyInput i)
        {
            Interlocked.Increment(ref _verifyCalls);
            return Task.FromResult(VerifyResponder(i));
        }

        [Activity]
        public Task<CoverageOutput> GradeCoverageAsync(CoverageInput i)
        {
            Interlocked.Increment(ref _coverageCalls);
            return Task.FromResult(CoverageResponder(i));
        }
    }
}
