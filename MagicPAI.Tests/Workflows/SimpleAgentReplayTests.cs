using FluentAssertions;
using Temporalio.Worker;
using MagicPAI.Server.Workflows;

namespace MagicPAI.Tests.Workflows;

/// <summary>
/// Replay-determinism guard for <see cref="SimpleAgentWorkflow"/>.
/// Runs the captured happy-path history through <see cref="WorkflowReplayer"/>;
/// any non-determinism introduced by refactors (DateTime.UtcNow, Guid.NewGuid
/// outside Workflow.Now/NewGuid, changed activity call order, etc.) will fail
/// this test. See temporal.md §15.5.
/// </summary>
[Trait("Category", "Replay")]
public class SimpleAgentReplayTests
{
    [Theory]
    [InlineData("Workflows/Histories/simple-agent/happy-path-v1.json")]
    [InlineData("Workflows/Histories/simple-agent/coverage-loop-v1.json")]
    public async Task ReplaysSuccessfully(string historyPath)
    {
        var absPath = Path.Combine(AppContext.BaseDirectory, historyPath);
        File.Exists(absPath).Should().BeTrue(
            because: $"replay fixture must exist at {absPath}. Run the " +
                     "integration test first to capture it.");

        var json = await File.ReadAllTextAsync(absPath);
        var history = Temporalio.Common.WorkflowHistory.FromJson(
            workflowId: "replay-simple-agent",
            json: json);

        var replayer = new WorkflowReplayer(
            new WorkflowReplayerOptions()
                .AddWorkflow<SimpleAgentWorkflow>());

        var result = await replayer.ReplayWorkflowAsync(history);

        result.ReplayFailure.Should().BeNull(
            because: "workflow code must deterministically replay this history. " +
                     "If this fails, a refactor introduced non-determinism (see temporal.md §25).");
    }
}
