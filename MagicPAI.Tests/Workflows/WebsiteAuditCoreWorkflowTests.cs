using FluentAssertions;
using Temporalio.Activities;
using Temporalio.Testing;
using Temporalio.Worker;
using MagicPAI.Activities.Contracts;
using MagicPAI.Server.Workflows;
using MagicPAI.Workflows.Contracts;

namespace MagicPAI.Tests.Workflows;

/// <summary>
/// Integration tests for the Temporal <see cref="WebsiteAuditCoreWorkflow"/>.
/// Stubs RunCliAgent to return a structured-output JSON blob with {report,
/// issueCount} and verifies the workflow extracts both fields correctly.
/// See temporal.md §H.11.
/// </summary>
[Trait("Category", "Integration")]
public class WebsiteAuditCoreWorkflowTests : IAsyncLifetime
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

    [Fact]
    public async Task ParsesStructuredOutput_IntoReportAndIssueCount()
    {
        var stubs = new WebsiteAuditCoreStubs
        {
            RunResponder = _ => new RunCliAgentOutput(
                Response: "raw text ignored when structured JSON present",
                StructuredOutputJson:
                    "{\"report\":\"Homepage has 3 heading-order issues.\",\"issueCount\":3}",
                Success: true,
                CostUsd: 0.15m,
                InputTokens: 50,
                OutputTokens: 100,
                FilesModified: Array.Empty<string>(),
                ExitCode: 0,
                AssistantSessionId: "stub"),
        };

        using var worker = new TemporalWorker(
            _env.Client,
            new TemporalWorkerOptions($"test-wac-{Guid.NewGuid():N}")
                .AddAllActivities(stubs)
                .AddWorkflow<WebsiteAuditCoreWorkflow>());

        await worker.ExecuteAsync(async () =>
        {
            var input = new WebsiteAuditCoreInput(
                SessionId: "wac-happy",
                SectionId: "homepage",
                SectionDescription: "Audit the homepage.",
                ContainerId: "cid-1",
                WorkspacePath: "/workspace",
                AiAssistant: "claude",
                Model: null);

            var handle = await _env.Client.StartWorkflowAsync(
                (WebsiteAuditCoreWorkflow wf) => wf.RunAsync(input),
                new(id: $"wac-happy-{Guid.NewGuid():N}",
                    taskQueue: worker.Options.TaskQueue!));

            var result = await handle.GetResultAsync();

            result.SectionId.Should().Be("homepage");
            result.AuditReport.Should().Be("Homepage has 3 heading-order issues.");
            result.IssueCount.Should().Be(3);
            result.CostUsd.Should().Be(0.15m);

            await ReplayFixtureHelper.CaptureIfMissingAsync(
                handle, "website-audit-core", "happy-path-v1.json");
        });
    }

    public class WebsiteAuditCoreStubs
    {
        public Func<RunCliAgentInput, RunCliAgentOutput> RunResponder { get; set; } =
            _ => new RunCliAgentOutput(
                Response: "",
                StructuredOutputJson: "{\"report\":\"default\",\"issueCount\":0}",
                Success: true,
                CostUsd: 0m,
                InputTokens: 0,
                OutputTokens: 0,
                FilesModified: Array.Empty<string>(),
                ExitCode: 0,
                AssistantSessionId: null);

        [Activity]
        public Task<RunCliAgentOutput> RunCliAgentAsync(RunCliAgentInput i) =>
            Task.FromResult(RunResponder(i));
    }
}
