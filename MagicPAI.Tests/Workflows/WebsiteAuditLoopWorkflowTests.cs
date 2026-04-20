using FluentAssertions;
using Temporalio.Activities;
using Temporalio.Testing;
using Temporalio.Worker;
using MagicPAI.Activities.Contracts;
using MagicPAI.Server.Workflows;
using MagicPAI.Workflows.Contracts;

namespace MagicPAI.Tests.Workflows;

/// <summary>
/// Integration tests for the Temporal <see cref="WebsiteAuditLoopWorkflow"/>.
/// Dispatches <see cref="WebsiteAuditCoreWorkflow"/> as a child once per
/// section; we register both workflows and stub RunCliAgent to return
/// per-section structured JSON. See temporal.md §H.12.
/// </summary>
[Trait("Category", "Integration")]
public class WebsiteAuditLoopWorkflowTests : IAsyncLifetime
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
    /// Happy path — three explicit sections; each yields 1 issue. Expect
    /// SectionsAudited=3, TotalIssueCount=3, total cost across all children.
    /// </summary>
    [Fact]
    public async Task AuditsAllSections_InOrder()
    {
        var stubs = new LoopStubs();

        using var worker = new TemporalWorker(
            _env.Client,
            new TemporalWorkerOptions($"test-wal-{Guid.NewGuid():N}")
                .AddAllActivities(stubs)
                .AddWorkflow<WebsiteAuditLoopWorkflow>()
                .AddWorkflow<WebsiteAuditCoreWorkflow>());

        await worker.ExecuteAsync(async () =>
        {
            var input = new WebsiteAuditInput(
                SessionId: "wal-happy",
                ContainerId: "cid-1",
                Prompt: "audit this site",
                WorkspacePath: "/workspace",
                AiAssistant: "claude",
                Model: null,
                SectionIds: new[] { "homepage", "navigation", "footer" });

            var handle = await _env.Client.StartWorkflowAsync(
                (WebsiteAuditLoopWorkflow wf) => wf.RunAsync(input),
                new(id: $"wal-happy-{Guid.NewGuid():N}",
                    taskQueue: worker.Options.TaskQueue!));

            var result = await handle.GetResultAsync();

            result.SectionsAudited.Should().Be(3);
            result.TotalIssueCount.Should().Be(3);   // 1 per section
            result.CostUsd.Should().Be(0.30m);       // 3 x 0.10
            result.Summary.Should().Contain("## homepage");
            result.Summary.Should().Contain("## navigation");
            result.Summary.Should().Contain("## footer");
            stubs.RunCliAgentCallCount.Should().Be(3);

            await ReplayFixtureHelper.CaptureIfMissingAsync(
                handle, "website-audit-loop", "happy-path-v1.json");
        });
    }

    public class LoopStubs
    {
        public int RunCliAgentCallCount { get; private set; }

        [Activity]
        public Task<RunCliAgentOutput> RunCliAgentAsync(RunCliAgentInput i)
        {
            RunCliAgentCallCount++;
            // Craft a structured JSON payload whose "report" echoes the prompt
            // so we can assert ordering via the Summary.
            var sectionTag = i.Prompt.Contains("homepage") ? "homepage"
                : i.Prompt.Contains("navigation") ? "navigation"
                : "footer";
            var json = $"{{\"report\":\"Audit for {sectionTag}\",\"issueCount\":1}}";
            return Task.FromResult(new RunCliAgentOutput(
                Response: json,
                StructuredOutputJson: json,
                Success: true,
                CostUsd: 0.10m,
                InputTokens: 50,
                OutputTokens: 80,
                FilesModified: Array.Empty<string>(),
                ExitCode: 0,
                AssistantSessionId: "stub"));
        }
    }
}
