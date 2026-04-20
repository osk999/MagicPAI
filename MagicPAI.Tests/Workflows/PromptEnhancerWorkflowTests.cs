using FluentAssertions;
using Temporalio.Activities;
using Temporalio.Testing;
using Temporalio.Worker;
using MagicPAI.Activities.Contracts;
using MagicPAI.Server.Workflows;
using MagicPAI.Workflows.Contracts;

namespace MagicPAI.Tests.Workflows;

/// <summary>
/// Integration test for the Temporal <see cref="PromptEnhancerWorkflow"/>. Thin
/// wrapper around <c>AiActivities.EnhancePromptAsync</c>, so the test just stubs
/// that one activity and verifies the workflow returns the stubbed output shape.
/// </summary>
[Trait("Category", "Integration")]
public class PromptEnhancerWorkflowTests : IAsyncLifetime
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
    /// Happy path — workflow forwards to the activity and maps its result into
    /// <see cref="PromptEnhancerOutput"/>. Also asserts the default instruction is
    /// applied when the caller omits one.
    /// </summary>
    [Fact]
    public async Task EnhancesPrompt_HappyPath()
    {
        EnhancePromptInput? capturedInput = null;
        var stubs = new PromptEnhancerStubs
        {
            EnhanceResponder = i =>
            {
                capturedInput = i;
                return new EnhancePromptOutput(
                    EnhancedPrompt: "enhanced",
                    WasEnhanced: true,
                    Rationale: "clarified intent");
            }
        };

        using var worker = new TemporalWorker(
            _env.Client,
            new TemporalWorkerOptions($"test-pe-happy-{Guid.NewGuid():N}")
                .AddAllActivities(stubs)
                .AddWorkflow<PromptEnhancerWorkflow>());

        await worker.ExecuteAsync(async () =>
        {
            var input = new PromptEnhancerInput(
                SessionId: "pe-happy",
                OriginalPrompt: "fix thing",
                ContainerId: "fake-cid",
                AiAssistant: "claude",
                ModelPower: 2);

            var handle = await _env.Client.StartWorkflowAsync(
                (PromptEnhancerWorkflow wf) => wf.RunAsync(input),
                new(id: $"pe-happy-{Guid.NewGuid():N}", taskQueue: worker.Options.TaskQueue!));

            var result = await handle.GetResultAsync();

            result.EnhancedPrompt.Should().Be("enhanced");
            result.WasEnhanced.Should().BeTrue();
            result.Rationale.Should().Be("clarified intent");
            result.CostUsd.Should().Be(0m);   // cost intentionally routed via side channel

            capturedInput.Should().NotBeNull();
            capturedInput!.OriginalPrompt.Should().Be("fix thing");
            capturedInput.EnhancementInstructions.Should().Contain("preserve intent");
            capturedInput.ContainerId.Should().Be("fake-cid");
            capturedInput.ModelPower.Should().Be(2);
            capturedInput.AiAssistant.Should().Be("claude");

            await ReplayFixtureHelper.CaptureIfMissingAsync(
                handle, "prompt-enhancer", "happy-path-v1.json");
        });
    }

    /// <summary>
    /// Stub implementation of <c>AiActivities.EnhancePromptAsync</c> — the only
    /// activity invoked by this workflow.
    /// </summary>
    public class PromptEnhancerStubs
    {
        public Func<EnhancePromptInput, EnhancePromptOutput> EnhanceResponder { get; set; } =
            _ => new EnhancePromptOutput(
                EnhancedPrompt: "stub-enhanced",
                WasEnhanced: true,
                Rationale: "stub");

        [Activity]
        public Task<EnhancePromptOutput> EnhancePromptAsync(EnhancePromptInput i) =>
            Task.FromResult(EnhanceResponder(i));
    }
}
