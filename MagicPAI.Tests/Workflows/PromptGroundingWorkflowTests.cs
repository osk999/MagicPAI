using FluentAssertions;
using Temporalio.Activities;
using Temporalio.Testing;
using Temporalio.Worker;
using MagicPAI.Activities.Contracts;
using MagicPAI.Server.Workflows;
using MagicPAI.Workflows.Contracts;

namespace MagicPAI.Tests.Workflows;

/// <summary>
/// Integration test for the Temporal <see cref="PromptGroundingWorkflow"/>. Exercises
/// a child workflow invocation (<see cref="ContextGathererWorkflow"/>) plus one activity
/// call (<c>AiActivities.EnhancePromptAsync</c>). Both the parent + child workflow
/// types and the stub activities are registered with the same worker so the test
/// environment drives both legs deterministically.
/// </summary>
[Trait("Category", "Integration")]
public class PromptGroundingWorkflowTests : IAsyncLifetime
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
    /// Happy path — assert the enhance-prompt activity receives an instruction that
    /// embeds the context gathered by the child workflow, and the workflow returns
    /// the enhanced prompt as its grounded output.
    /// </summary>
    [Fact]
    public async Task Grounds_Prompt_WithChildContextWorkflow()
    {
        EnhancePromptInput? capturedEnhanceInput = null;
        var stubs = new PromptGroundingStubs
        {
            ResearchResponder = _ => new ResearchPromptOutput(
                EnhancedPrompt: "child-rewrite",
                CodebaseAnalysis: "File X",
                ResearchContext: "Framework Y",
                Rationale: "ok"),
            EnhanceResponder = i =>
            {
                capturedEnhanceInput = i;
                return new EnhancePromptOutput(
                    EnhancedPrompt: "grounded prompt",
                    WasEnhanced: true,
                    Rationale: "uses context");
            }
        };

        using var worker = new TemporalWorker(
            _env.Client,
            new TemporalWorkerOptions($"test-pg-happy-{Guid.NewGuid():N}")
                .AddAllActivities(stubs)
                // Register BOTH the parent and child workflow types — the test
                // environment is the only worker running, so the child workflow
                // invocation needs its implementation here as well.
                .AddWorkflow<PromptGroundingWorkflow>()
                .AddWorkflow<ContextGathererWorkflow>());

        await worker.ExecuteAsync(async () =>
        {
            var input = new PromptGroundingInput(
                SessionId: "pg-happy",
                Prompt: "ground me",
                ContainerId: "fake-cid",
                WorkingDirectory: "/workspace",
                AiAssistant: "claude");

            var handle = await _env.Client.StartWorkflowAsync(
                (PromptGroundingWorkflow wf) => wf.RunAsync(input),
                new(id: $"pg-happy-{Guid.NewGuid():N}", taskQueue: worker.Options.TaskQueue!));

            var result = await handle.GetResultAsync();

            result.GroundedPrompt.Should().Be("grounded prompt");
            result.Rationale.Should().Be("uses context");
            result.CostUsd.Should().Be(0m);

            // EnhancePrompt should have been called with an instruction that
            // includes the gathered context (File X / Framework Y).
            capturedEnhanceInput.Should().NotBeNull();
            capturedEnhanceInput!.OriginalPrompt.Should().Be("ground me");
            capturedEnhanceInput.EnhancementInstructions.Should()
                .Contain("File X").And.Contain("Framework Y");

            await ReplayFixtureHelper.CaptureIfMissingAsync(
                handle, "prompt-grounding", "happy-path-v1.json");
        });
    }

    /// <summary>
    /// Stubs every activity invoked along the parent + child workflow chain:
    /// <list type="bullet">
    ///   <item><c>ResearchPromptAsync</c> — used by <c>ContextGathererWorkflow</c>.</item>
    ///   <item><c>RunCliAgentAsync</c> — used by <c>ContextGathererWorkflow</c>'s
    ///     repo-map and memory-recall passes (added in the parallel fan-out fix).</item>
    ///   <item><c>EnhancePromptAsync</c> — used by <c>PromptGroundingWorkflow</c>.</item>
    /// </list>
    /// </summary>
    public class PromptGroundingStubs
    {
        public Func<ResearchPromptInput, ResearchPromptOutput> ResearchResponder { get; set; } =
            _ => new ResearchPromptOutput(
                EnhancedPrompt: "stub-rewrite",
                CodebaseAnalysis: "stub-codebase",
                ResearchContext: "stub-research",
                Rationale: "stub");

        public Func<EnhancePromptInput, EnhancePromptOutput> EnhanceResponder { get; set; } =
            _ => new EnhancePromptOutput(
                EnhancedPrompt: "stub-enhanced",
                WasEnhanced: true,
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
        public Task<EnhancePromptOutput> EnhancePromptAsync(EnhancePromptInput i) =>
            Task.FromResult(EnhanceResponder(i));

        [Activity]
        public Task<RunCliAgentOutput> RunCliAgentAsync(RunCliAgentInput i) =>
            Task.FromResult(CliResponder(i));
    }
}
