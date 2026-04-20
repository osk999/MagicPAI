using FluentAssertions;
using Temporalio.Worker;
using MagicPAI.Server.Workflows;

namespace MagicPAI.Tests.Workflows;

/// <summary>
/// Replay-determinism guard for <see cref="WebsiteAuditLoopWorkflow"/>. See
/// temporal.md §15.5. The loop dispatches <see cref="WebsiteAuditCoreWorkflow"/>
/// children so both types are registered.
/// </summary>
[Trait("Category", "Replay")]
public class WebsiteAuditLoopReplayTests
{
    [Theory]
    [InlineData("Workflows/Histories/website-audit-loop/happy-path-v1.json")]
    public async Task ReplaysSuccessfully(string historyPath)
    {
        var absPath = Path.Combine(AppContext.BaseDirectory, historyPath);
        File.Exists(absPath).Should().BeTrue(
            because: $"replay fixture must exist at {absPath}. Run the " +
                     "integration test first to capture it.");

        var json = await File.ReadAllTextAsync(absPath);
        var history = Temporalio.Common.WorkflowHistory.FromJson(
            workflowId: "replay-website-audit-loop",
            json: json);

        var replayer = new WorkflowReplayer(
            new WorkflowReplayerOptions()
                .AddWorkflow<WebsiteAuditLoopWorkflow>()
                .AddWorkflow<WebsiteAuditCoreWorkflow>());

        var result = await replayer.ReplayWorkflowAsync(history);

        result.ReplayFailure.Should().BeNull(
            because: "workflow code must deterministically replay this history.");
    }
}
