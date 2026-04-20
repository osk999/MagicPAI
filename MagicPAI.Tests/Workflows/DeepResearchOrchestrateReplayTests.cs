using FluentAssertions;
using Temporalio.Worker;
using MagicPAI.Server.Workflows;

namespace MagicPAI.Tests.Workflows;

/// <summary>
/// Replay-determinism guard for <see cref="DeepResearchOrchestrateWorkflow"/>.
/// See temporal.md §15.5. The orchestrator chains
/// <see cref="ResearchPipelineWorkflow"/> → <see cref="StandardOrchestrateWorkflow"/>
/// → <see cref="VerifyAndRepairWorkflow"/> so all four types are registered.
/// </summary>
[Trait("Category", "Replay")]
public class DeepResearchOrchestrateReplayTests
{
    [Theory]
    [InlineData("Workflows/Histories/deep-research-orchestrate/happy-path-v1.json")]
    public async Task ReplaysSuccessfully(string historyPath)
    {
        var absPath = Path.Combine(AppContext.BaseDirectory, historyPath);
        File.Exists(absPath).Should().BeTrue(
            because: $"replay fixture must exist at {absPath}. Run the " +
                     "integration test first to capture it.");

        var json = await File.ReadAllTextAsync(absPath);
        var history = Temporalio.Common.WorkflowHistory.FromJson(
            workflowId: "replay-deep-research-orchestrate",
            json: json);

        var replayer = new WorkflowReplayer(
            new WorkflowReplayerOptions()
                .AddWorkflow<DeepResearchOrchestrateWorkflow>()
                .AddWorkflow<ResearchPipelineWorkflow>()
                .AddWorkflow<StandardOrchestrateWorkflow>()
                .AddWorkflow<VerifyAndRepairWorkflow>());

        var result = await replayer.ReplayWorkflowAsync(history);

        result.ReplayFailure.Should().BeNull(
            because: "workflow code must deterministically replay this history.");
    }
}
