using FluentAssertions;
using Temporalio.Worker;
using MagicPAI.Server.Workflows;

namespace MagicPAI.Tests.Workflows;

/// <summary>
/// Replay-determinism guard for <see cref="PromptGroundingWorkflow"/>. See
/// temporal.md §15.5. The workflow invokes <see cref="ContextGathererWorkflow"/>
/// as a child so both types are registered on the replayer.
/// </summary>
[Trait("Category", "Replay")]
public class PromptGroundingReplayTests
{
    [Theory]
    [InlineData("Workflows/Histories/prompt-grounding/happy-path-v1.json")]
    public async Task ReplaysSuccessfully(string historyPath)
    {
        var absPath = Path.Combine(AppContext.BaseDirectory, historyPath);
        File.Exists(absPath).Should().BeTrue(
            because: $"replay fixture must exist at {absPath}. Run the " +
                     "integration test first to capture it.");

        var json = await File.ReadAllTextAsync(absPath);
        var history = Temporalio.Common.WorkflowHistory.FromJson(
            workflowId: "replay-prompt-grounding",
            json: json);

        var replayer = new WorkflowReplayer(
            new WorkflowReplayerOptions()
                .AddWorkflow<PromptGroundingWorkflow>()
                .AddWorkflow<ContextGathererWorkflow>());

        var result = await replayer.ReplayWorkflowAsync(history);

        result.ReplayFailure.Should().BeNull(
            because: "workflow code must deterministically replay this history.");
    }
}
