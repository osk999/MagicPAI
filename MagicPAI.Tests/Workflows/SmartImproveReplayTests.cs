using FluentAssertions;
using Temporalio.Testing;
using Temporalio.Worker;
using MagicPAI.Activities.Contracts;
using MagicPAI.Server.Workflows;
using MagicPAI.Workflows.Contracts;

namespace MagicPAI.Tests.Workflows;

/// <summary>
/// Replay-determinism guard for <see cref="SmartImproveWorkflow"/>. Captures
/// the verified-clean termination history and asserts deterministic replay.
/// See temporal.md §15.5 + §20 + CLAUDE.md §"Workflow versioning".
/// </summary>
[Trait("Category", "Replay")]
public class SmartImproveReplayTests : IAsyncLifetime
{
    private WorkflowEnvironment _env = null!;

    public async Task InitializeAsync() =>
        _env = await WorkflowEnvironment.StartTimeSkippingAsync();

    public async Task DisposeAsync()
    {
        if (_env is not null) await _env.ShutdownAsync();
    }

    [Fact]
    public async Task VerifiedClean_ReplaysDeterministically()
    {
        const string historyFile = "Workflows/Histories/smart-improve/verified-clean-v1.json";
        await EnsureFixtureAsync(historyFile);

        var absPath = Path.Combine(AppContext.BaseDirectory, historyFile);
        File.Exists(absPath).Should().BeTrue();

        var json = await File.ReadAllTextAsync(absPath);
        var history = Temporalio.Common.WorkflowHistory.FromJson(
            workflowId: "replay-smart-improve",
            json: json);

        var replayer = new WorkflowReplayer(
            new WorkflowReplayerOptions()
                .AddWorkflow<SmartImproveWorkflow>()
                // SmartImprove dispatches these as child workflows; the
                // replayer needs them registered so child-workflow start
                // commands resolve.
                .AddWorkflow<ContextGathererWorkflow>()
                .AddWorkflow<SmartIterativeLoopWorkflow>());

        var result = await replayer.ReplayWorkflowAsync(history);
        result.ReplayFailure.Should().BeNull(
            because: "SmartImproveWorkflow must replay deterministically. " +
                     "Failure indicates a refactor introduced non-determinism " +
                     "(see CLAUDE.md §Workflow versioning).");
    }

    private async Task EnsureFixtureAsync(string historyFile)
    {
        var stubs = new SmartImproveWorkflowTests.Stubs();
        // Verifier returns clean every call → 2 consecutive bursts both
        // clean → exit "verified-clean" with 2 bursts completed.
        stubs.VerifyResponder = _ => new VerifyHarnessOutput(
            0, 0, 0, 0, 0, 0, Array.Empty<RubricFailure>(), "");

        using var worker = new TemporalWorker(
            _env.Client,
            new TemporalWorkerOptions($"replay-cap-si-{Guid.NewGuid():N}")
                .AddAllActivities(stubs)
                .AddAllActivities(new StageActivityStubs())
                .AddWorkflow<SmartImproveWorkflow>()
                .AddWorkflow<ContextGathererWorkflow>()
                .AddWorkflow<SmartIterativeLoopWorkflow>());

        var input = new SmartImproveInput(
            SessionId: "replay-fixture-si",
            Prompt: "Capture history for replay test.",
            AiAssistant: "claude",
            WorkspacePath: "/workspace",
            MaxTotalIterations: 100,
            MaxBursts: 5,
            RequiredCleanVerifies: 2);

        await worker.ExecuteAsync(async () =>
        {
            var handle = await _env.Client.StartWorkflowAsync(
                (SmartImproveWorkflow w) => w.RunAsync(input),
                new(id: $"replay-cap-si-{Guid.NewGuid():N}", taskQueue: worker.Options.TaskQueue!));
            await handle.GetResultAsync();

            await ReplayFixtureHelper.CaptureIfMissingAsync(
                handle, "smart-improve", Path.GetFileName(historyFile));
        });
    }
}
