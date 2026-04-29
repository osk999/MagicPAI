using FluentAssertions;
using Temporalio.Activities;
using Temporalio.Testing;
using Temporalio.Worker;
using MagicPAI.Activities.Contracts;
using MagicPAI.Server.Workflows;
using MagicPAI.Workflows.Contracts;

namespace MagicPAI.Tests.Workflows;

/// <summary>
/// Integration test for the Temporal <see cref="ContextGathererWorkflow"/>. The
/// workflow fans out three context-gathering probes in parallel
/// (<c>AiActivities.ResearchPromptAsync</c>, plus two
/// <c>AiActivities.RunCliAgentAsync</c> calls — one repo-map and one memory
/// recall) and stitches them into a single section-tagged blob. The test
/// asserts every section appears and that the costs from both CLI passes are
/// summed.
/// </summary>
[Trait("Category", "Integration")]
public class ContextGathererWorkflowTests : IAsyncLifetime
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
    /// Happy path — three probes run concurrently, GatheredContext carries every
    /// section header, and CostUsd sums the two CLI passes (research has no
    /// cost field).
    /// </summary>
    [Fact]
    public async Task GathersContext_HappyPath()
    {
        ResearchPromptInput? capturedResearch = null;
        var capturedCli = new List<RunCliAgentInput>();
        var stubs = new ContextGathererStubs
        {
            ResearchResponder = i =>
            {
                capturedResearch = i;
                return new ResearchPromptOutput(
                    EnhancedPrompt: "rewritten",
                    CodebaseAnalysis: "File A, File B",
                    ResearchContext: "Relevant APIs",
                    Rationale: "analyzed");
            },
            CliResponder = i =>
            {
                capturedCli.Add(i);
                // Distinguish the two cheap calls by prompt prefix so the
                // assertion that BOTH end up in the gathered context is meaningful.
                var body = i.Prompt.StartsWith("List every top-level directory", StringComparison.Ordinal)
                    ? "- src/\n- tests/\n- docs/"
                    : "Use Workflow.UtcNow, never DateTime.Now.";
                return new RunCliAgentOutput(
                    Response: body,
                    StructuredOutputJson: null,
                    Success: true,
                    CostUsd: 0.05m,
                    InputTokens: 50,
                    OutputTokens: 50,
                    FilesModified: Array.Empty<string>(),
                    ExitCode: 0,
                    AssistantSessionId: null);
            }
        };

        using var worker = new TemporalWorker(
            _env.Client,
            new TemporalWorkerOptions($"test-cg-happy-{Guid.NewGuid():N}")
                .AddAllActivities(stubs)
                .AddAllActivities(new StageActivityStubs())
                .AddWorkflow<ContextGathererWorkflow>());

        await worker.ExecuteAsync(async () =>
        {
            var input = new ContextGathererInput(
                SessionId: "cg-happy",
                Prompt: "find context",
                ContainerId: "fake-cid",
                WorkingDirectory: "/workspace",
                AiAssistant: "claude");

            var handle = await _env.Client.StartWorkflowAsync(
                (ContextGathererWorkflow wf) => wf.RunAsync(input),
                new(id: $"cg-happy-{Guid.NewGuid():N}", taskQueue: worker.Options.TaskQueue!));

            var result = await handle.GetResultAsync();

            // Section headers from the new fan-out shape.
            result.GatheredContext.Should().Contain("# Codebase Analysis");
            result.GatheredContext.Should().Contain("File A, File B");
            result.GatheredContext.Should().Contain("# Research Context");
            result.GatheredContext.Should().Contain("Relevant APIs");
            result.GatheredContext.Should().Contain("# Repository Map");
            result.GatheredContext.Should().Contain("- src/");
            result.GatheredContext.Should().Contain("# Relevant Memory");
            result.GatheredContext.Should().Contain("Workflow.UtcNow");

            result.ReferencedFiles.Should().BeEmpty();
            // Repo-map (0.05) + memory (0.05) = 0.10 — research has no cost.
            result.CostUsd.Should().Be(0.10m);

            // Research call: power 2 (balanced).
            capturedResearch.Should().NotBeNull();
            capturedResearch!.Prompt.Should().Be("find context");
            capturedResearch.ContainerId.Should().Be("fake-cid");
            capturedResearch.ModelPower.Should().Be(2);
            capturedResearch.AiAssistant.Should().Be("claude");

            // Both CLI calls: same container, power 3 (cheapest), MaxTurns 3.
            capturedCli.Should().HaveCount(2);
            capturedCli.Should().OnlyContain(c => c.ContainerId == "fake-cid");
            capturedCli.Should().OnlyContain(c => c.ModelPower == 3);
            capturedCli.Should().OnlyContain(c => c.MaxTurns == 3);
            capturedCli.Should().Contain(c => c.Prompt.StartsWith("List every top-level directory", StringComparison.Ordinal));
            capturedCli.Should().Contain(c => c.Prompt.StartsWith("Scan CLAUDE.md", StringComparison.Ordinal));

            await ReplayFixtureHelper.CaptureIfMissingAsync(
                handle, "context-gatherer", "happy-path-v1.json");
        });
    }

    /// <summary>
    /// Stub bag for the three activities the workflow now invokes.
    /// </summary>
    public class ContextGathererStubs
    {
        public Func<ResearchPromptInput, ResearchPromptOutput> ResearchResponder { get; set; } =
            _ => new ResearchPromptOutput(
                EnhancedPrompt: "stub-rewrite",
                CodebaseAnalysis: "stub-codebase",
                ResearchContext: "stub-research",
                Rationale: "stub");

        public Func<RunCliAgentInput, RunCliAgentOutput> CliResponder { get; set; } =
            _ => new RunCliAgentOutput(
                Response: "stub-cli",
                StructuredOutputJson: null,
                Success: true,
                CostUsd: 0m,
                InputTokens: 0,
                OutputTokens: 0,
                FilesModified: Array.Empty<string>(),
                ExitCode: 0,
                AssistantSessionId: null);

        [Activity]
        public Task<ResearchPromptOutput> ResearchPromptAsync(ResearchPromptInput i) =>
            Task.FromResult(ResearchResponder(i));

        [Activity]
        public Task<RunCliAgentOutput> RunCliAgentAsync(RunCliAgentInput i) =>
            Task.FromResult(CliResponder(i));
    }
}
