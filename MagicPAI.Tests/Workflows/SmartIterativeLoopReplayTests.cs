using FluentAssertions;
using Temporalio.Activities;
using Temporalio.Testing;
using Temporalio.Worker;
using MagicPAI.Activities.Contracts;
using MagicPAI.Server.Workflows;
using MagicPAI.Workflows.Contracts;

namespace MagicPAI.Tests.Workflows;

/// <summary>
/// Replay-determinism guard for <see cref="SmartIterativeLoopWorkflow"/>.
/// Captures a real workflow history (silence-confirmed termination) on
/// first run and asserts it replays without non-determinism on subsequent
/// runs. Mirrors the pattern used by SimpleAgentReplayTests.
///
/// If this test fails after a workflow refactor, the most likely causes are:
///   1. Adding/removing/reordering an activity call without
///      <c>Workflow.Patched("change-id-vN")</c>
///   2. Calling DateTime.UtcNow / Guid.NewGuid / Random / Task.Delay
///      from workflow body (must use Workflow.* equivalents)
///   3. Changing the conditional branches that depend on input fields
///
/// See temporal.md §15.5 + §20 + CLAUDE.md §"Workflow versioning".
/// </summary>
[Trait("Category", "Replay")]
public class SmartIterativeLoopReplayTests : IAsyncLifetime
{
    private WorkflowEnvironment _env = null!;

    public async Task InitializeAsync() =>
        _env = await WorkflowEnvironment.StartTimeSkippingAsync();

    public async Task DisposeAsync()
    {
        if (_env is not null) await _env.ShutdownAsync();
    }

    [Fact]
    public async Task SilenceConfirmed_ReplaysDeterministically()
    {
        const string historyFile = "Workflows/Histories/smart-iterative-loop/silence-confirmed-v1.json";

        // First, capture the history (idempotent — only writes if file missing).
        await EnsureFixtureAsync(historyFile);

        var absPath = Path.Combine(AppContext.BaseDirectory, historyFile);
        File.Exists(absPath).Should().BeTrue(
            because: $"replay fixture must exist at {absPath} after capture step");

        var json = await File.ReadAllTextAsync(absPath);
        var history = Temporalio.Common.WorkflowHistory.FromJson(
            workflowId: "replay-smart-iterative-loop",
            json: json);

        var replayer = new WorkflowReplayer(
            new WorkflowReplayerOptions()
                .AddWorkflow<SmartIterativeLoopWorkflow>());

        var result = await replayer.ReplayWorkflowAsync(history);

        result.ReplayFailure.Should().BeNull(
            because: "workflow must deterministically replay this history. " +
                     "If this fails, a refactor introduced non-determinism — " +
                     "see CLAUDE.md §Workflow versioning.");
    }

    [Fact]
    public async Task NoProgress_ReplaysDeterministically()
    {
        const string historyFile = "Workflows/Histories/smart-iterative-loop/no-progress-v1.json";

        await EnsureNoProgressFixtureAsync(historyFile);

        var absPath = Path.Combine(AppContext.BaseDirectory, historyFile);
        File.Exists(absPath).Should().BeTrue();

        var json = await File.ReadAllTextAsync(absPath);
        var history = Temporalio.Common.WorkflowHistory.FromJson(
            workflowId: "replay-smart-iterative-loop-no-progress",
            json: json);

        var replayer = new WorkflowReplayer(
            new WorkflowReplayerOptions()
                .AddWorkflow<SmartIterativeLoopWorkflow>());

        var result = await replayer.ReplayWorkflowAsync(history);
        result.ReplayFailure.Should().BeNull();
    }

    // ── Capture helpers ────────────────────────────────────────────────

    private async Task EnsureFixtureAsync(string historyFile)
    {
        var stubs = new SmartIterativeLoopWorkflowTests.Stubs();
        var responses = new Queue<string>(new[]
        {
            "Did some work.\n[DONE]",
            "Nothing left.",
            "Confirmed nothing left.",
        });
        stubs.RunResponder = _ => new RunCliAgentOutput(
            Response: responses.Count == 0 ? "fallback" : responses.Dequeue(),
            StructuredOutputJson: null, Success: true,
            CostUsd: 0.01m, InputTokens: 1, OutputTokens: 1,
            FilesModified: Array.Empty<string>(), ExitCode: 0,
            AssistantSessionId: "stub");
        // Stable filesystem → silence countdown can complete.
        stubs.FsResponder = _ => new SnapshotFilesystemOutput(
            new Dictionary<string, string> { ["src/foo.cs"] = "stable-hash" },
            1, 1, false);

        using var worker = new TemporalWorker(
            _env.Client,
            new TemporalWorkerOptions($"replay-cap-{Guid.NewGuid():N}")
                .AddAllActivities(stubs)
                .AddAllActivities(new StageActivityStubs())
                .AddWorkflow<SmartIterativeLoopWorkflow>());

        var input = new SmartIterativeLoopInput(
            SessionId: "replay-fixture",
            ContainerId: "ctr-fixture",
            WorkspacePath: "/workspace",
            Prompt: "Capture history.",
            AiAssistant: "claude",
            Model: null,
            ModelPower: 2,
            MaxIterations: 10,
            MinIterations: 1);

        await worker.ExecuteAsync(async () =>
        {
            var handle = await _env.Client.StartWorkflowAsync(
                (SmartIterativeLoopWorkflow w) => w.RunAsync(input),
                new(id: $"replay-cap-{Guid.NewGuid():N}", taskQueue: worker.Options.TaskQueue!));
            await handle.GetResultAsync();

            await ReplayFixtureHelper.CaptureIfMissingAsync(
                handle, "smart-iterative-loop", Path.GetFileName(historyFile));
        });
    }

    private async Task EnsureNoProgressFixtureAsync(string historyFile)
    {
        var stubs = new SmartIterativeLoopWorkflowTests.Stubs();
        // Model never emits [DONE]; same git HEAD + same AST hash every iter
        // → both no-progress signals fire, exits "no-progress" after threshold.
        stubs.RunResponder = _ => new RunCliAgentOutput(
            Response: "doing nothing",
            StructuredOutputJson: null, Success: true,
            CostUsd: 0.01m, InputTokens: 1, OutputTokens: 1,
            FilesModified: Array.Empty<string>(), ExitCode: 0,
            AssistantSessionId: "stub");
        stubs.GitResponder = _ => new GetGitStateOutput("sha-stable", 0, true, false);
        stubs.AstResponder = _ => new ComputeAstHashOutput("ast-stable", 1, false);
        stubs.FsResponder = _ => new SnapshotFilesystemOutput(
            new Dictionary<string, string> { ["src/foo.cs"] = "fs-stable" },
            1, 1, false);

        using var worker = new TemporalWorker(
            _env.Client,
            new TemporalWorkerOptions($"replay-cap-np-{Guid.NewGuid():N}")
                .AddAllActivities(stubs)
                .AddAllActivities(new StageActivityStubs())
                .AddWorkflow<SmartIterativeLoopWorkflow>());

        var input = new SmartIterativeLoopInput(
            SessionId: "replay-np",
            ContainerId: "ctr-np",
            WorkspacePath: "/workspace",
            Prompt: "Test no-progress.",
            AiAssistant: "claude",
            Model: null,
            ModelPower: 2,
            MaxIterations: 20,
            MinIterations: 1,
            NoProgressThreshold: 3);

        await worker.ExecuteAsync(async () =>
        {
            var handle = await _env.Client.StartWorkflowAsync(
                (SmartIterativeLoopWorkflow w) => w.RunAsync(input),
                new(id: $"replay-cap-np-{Guid.NewGuid():N}", taskQueue: worker.Options.TaskQueue!));
            await handle.GetResultAsync();

            await ReplayFixtureHelper.CaptureIfMissingAsync(
                handle, "smart-iterative-loop", Path.GetFileName(historyFile));
        });
    }
}
