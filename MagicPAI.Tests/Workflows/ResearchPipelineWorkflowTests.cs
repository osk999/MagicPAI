using FluentAssertions;
using Temporalio.Activities;
using Temporalio.Testing;
using Temporalio.Worker;
using MagicPAI.Activities.Contracts;
using MagicPAI.Server.Workflows;
using MagicPAI.Workflows.Contracts;

namespace MagicPAI.Tests.Workflows;

/// <summary>
/// Integration tests for the Temporal <see cref="ResearchPipelineWorkflow"/>.
/// Single-activity workflow — asserts ResearchPrompt is invoked with
/// ModelPower=1 (strongest model) and the output fields are projected through.
/// See temporal.md §H.8.
/// </summary>
[Trait("Category", "Integration")]
public class ResearchPipelineWorkflowTests : IAsyncLifetime
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
    public async Task Completes_UsingStrongestModel()
    {
        var stubs = new ResearchPipelineStubs();

        using var worker = new TemporalWorker(
            _env.Client,
            new TemporalWorkerOptions($"test-research-{Guid.NewGuid():N}")
                .AddAllActivities(stubs)
                .AddWorkflow<ResearchPipelineWorkflow>());

        await worker.ExecuteAsync(async () =>
        {
            var input = new ResearchPipelineInput(
                SessionId: "rp-happy",
                Prompt: "refactor the auth layer",
                ContainerId: "cid-1",
                WorkingDirectory: "/workspace",
                AiAssistant: "claude");

            var handle = await _env.Client.StartWorkflowAsync(
                (ResearchPipelineWorkflow wf) => wf.RunAsync(input),
                new(id: $"rp-happy-{Guid.NewGuid():N}",
                    taskQueue: worker.Options.TaskQueue!));

            var result = await handle.GetResultAsync();

            result.ResearchedPrompt.Should().Be("enhanced: refactor the auth layer");
            result.ResearchContext.Should().Be("Found AuthService, TokenStore.");
            result.CostUsd.Should().Be(0m);

            // Strongest-model contract: activity is invoked with ModelPower=1.
            stubs.LastInput.Should().NotBeNull();
            stubs.LastInput!.ModelPower.Should().Be(1);
            stubs.ResearchCallCount.Should().Be(1);

            await ReplayFixtureHelper.CaptureIfMissingAsync(
                handle, "research-pipeline", "happy-path-v1.json");
        });
    }

    public class ResearchPipelineStubs
    {
        public ResearchPromptInput? LastInput { get; private set; }
        public int ResearchCallCount { get; private set; }

        public Func<ResearchPromptInput, ResearchPromptOutput> Responder { get; set; } =
            _ => new ResearchPromptOutput(
                EnhancedPrompt: "enhanced: refactor the auth layer",
                CodebaseAnalysis: "AuthService, TokenStore",
                ResearchContext: "Found AuthService, TokenStore.",
                Rationale: "grounded");

        [Activity]
        public Task<ResearchPromptOutput> ResearchPromptAsync(ResearchPromptInput i)
        {
            LastInput = i;
            ResearchCallCount++;
            return Task.FromResult(Responder(i));
        }
    }
}
