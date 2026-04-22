using FluentAssertions;
using Temporalio.Worker;
using MagicPAI.Server.Workflows;

namespace MagicPAI.Tests.Workflows;

/// <summary>
/// Replay-determinism guard for <see cref="FullOrchestrateWorkflow"/>. The
/// captured fixture is the simple-path branch (classifier returns false), so
/// only the <see cref="SimpleAgentWorkflow"/> child is exercised. See
/// temporal.md §15.5.
/// </summary>
[Trait("Category", "Replay")]
public class FullOrchestrateReplayTests
{
    [Theory]
    [InlineData("Workflows/Histories/full-orchestrate/happy-path-v1.json")]
    public async Task ReplaysSuccessfully(string historyPath)
    {
        var absPath = Path.Combine(AppContext.BaseDirectory, historyPath);
        File.Exists(absPath).Should().BeTrue(
            because: $"replay fixture must exist at {absPath}. Run the " +
                     "integration test first to capture it.");

        var json = await File.ReadAllTextAsync(absPath);
        var history = Temporalio.Common.WorkflowHistory.FromJson(
            workflowId: "replay-full-orchestrate",
            json: json);

        var replayer = new WorkflowReplayer(
            new WorkflowReplayerOptions()
                .AddWorkflow<FullOrchestrateWorkflow>()
                .AddWorkflow<SimpleAgentWorkflow>()
                .AddWorkflow<WebsiteAuditLoopWorkflow>()
                .AddWorkflow<WebsiteAuditCoreWorkflow>()
                .AddWorkflow<OrchestrateComplexPathWorkflow>()
                .AddWorkflow<ComplexTaskWorkerWorkflow>()
                .AddWorkflow<VerifyAndRepairWorkflow>());

        var result = await replayer.ReplayWorkflowAsync(history);

        result.ReplayFailure.Should().BeNull(
            because: "workflow code must deterministically replay this history.");
    }
}
