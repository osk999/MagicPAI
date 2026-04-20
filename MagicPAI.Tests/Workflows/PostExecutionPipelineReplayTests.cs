using FluentAssertions;
using Temporalio.Worker;
using MagicPAI.Server.Workflows;

namespace MagicPAI.Tests.Workflows;

/// <summary>
/// Replay-determinism guard for <see cref="PostExecutionPipelineWorkflow"/>.
/// See temporal.md §15.5.
/// </summary>
[Trait("Category", "Replay")]
public class PostExecutionPipelineReplayTests
{
    [Theory]
    [InlineData("Workflows/Histories/post-execution-pipeline/happy-path-v1.json")]
    public async Task ReplaysSuccessfully(string historyPath)
    {
        var absPath = Path.Combine(AppContext.BaseDirectory, historyPath);
        File.Exists(absPath).Should().BeTrue(
            because: $"replay fixture must exist at {absPath}. Run the " +
                     "integration test first to capture it.");

        var json = await File.ReadAllTextAsync(absPath);
        var history = Temporalio.Common.WorkflowHistory.FromJson(
            workflowId: "replay-post-execution-pipeline",
            json: json);

        var replayer = new WorkflowReplayer(
            new WorkflowReplayerOptions()
                .AddWorkflow<PostExecutionPipelineWorkflow>());

        var result = await replayer.ReplayWorkflowAsync(history);

        result.ReplayFailure.Should().BeNull(
            because: "workflow code must deterministically replay this history.");
    }
}
