using FluentAssertions;
using Temporalio.Activities;
using Temporalio.Testing;
using Temporalio.Worker;
using MagicPAI.Activities.Contracts;
using MagicPAI.Server.Workflows;
using MagicPAI.Workflows.Contracts;

namespace MagicPAI.Tests.Workflows;

[Trait("Category", "Integration")]
public class IterativeLoopWorkflowTests : IAsyncLifetime
{
    private WorkflowEnvironment _env = null!;

    public async Task InitializeAsync() =>
        _env = await WorkflowEnvironment.StartTimeSkippingAsync();

    public async Task DisposeAsync()
    {
        if (_env is not null) await _env.ShutdownAsync();
    }

    // ── Structured progress parser ─────────────────────────────────────

    [Fact]
    public void ParseProgress_AllChecked_WithFlagAndMarker_IsDone()
    {
        var text = """
            ### Task Status
            - [x] one — done
            - [x] two — done

            ### Completion
            Completion: true

            [DONE]
            """;
        var p = IterativeLoopWorkflow.ParseProgress(text, "[DONE]");
        p.TotalTasks.Should().Be(2);
        p.CompletedTasks.Should().Be(2);
        p.CompletionFlag.Should().BeTrue();
        p.MarkerPresent.Should().BeTrue();
        p.OpenTaskDescriptions.Should().BeEmpty();
    }

    [Fact]
    public void ParseProgress_OpenTask_IsNotDone()
    {
        var text = """
            - [x] one
            - [ ] two — still working
            Completion: false
            """;
        var p = IterativeLoopWorkflow.ParseProgress(text, "[DONE]");
        p.OpenTaskDescriptions.Should().ContainSingle().Which.Should().Contain("two");
        p.CompletionFlag.Should().BeFalse();
        p.MarkerPresent.Should().BeFalse();
    }

    [Fact]
    public void ParseProgress_MarkerInProse_NotOnOwnLine_IsNotDetected()
    {
        var text = "I plan to emit [DONE] at some point.\nCompletion: false";
        var p = IterativeLoopWorkflow.ParseProgress(text, "[DONE]");
        p.MarkerPresent.Should().BeFalse();
    }

    // ── Workflow loop behaviour (WorkflowEnvironment + stubs) ─────────

    [Fact]
    public async Task StopsAtFirstDone_WhenMinSatisfied()
    {
        var stubs = new Stubs();
        var responses = new Queue<string>(new[]
        {
            "working\n- [ ] task — pending\nCompletion: false",
            BuildDoneReport("task"),
        });
        stubs.RunResponder = _ => new RunCliAgentOutput(
            Response: responses.Dequeue(),
            StructuredOutputJson: null, Success: true,
            CostUsd: 0.01m, InputTokens: 1, OutputTokens: 1,
            FilesModified: Array.Empty<string>(), ExitCode: 0,
            AssistantSessionId: "stub-session");

        var result = await RunLoop(stubs, new IterativeLoopInput(
            SessionId: "s-stop-first-done",
            Prompt: "build x",
            AiAssistant: "claude",
            Model: null, ModelPower: 2,
            MinIterations: 1, MaxIterations: 5,
            CompletionStrategy: CompletionStrategy.StructuredProgress));

        result.ExitReason.Should().Be("done");
        result.IterationsRun.Should().Be(2);
        result.DoneMarkerObserved.Should().BeTrue();
        stubs.SpawnCalls.Should().Be(1);
        stubs.DestroyCalls.Should().Be(1);
    }

    [Fact]
    public async Task KeepsLooping_WhenDoneBeforeMin()
    {
        var stubs = new Stubs();
        stubs.RunResponder = _ => new RunCliAgentOutput(
            Response: BuildDoneReport("task"),   // always "done"
            StructuredOutputJson: null, Success: true,
            CostUsd: 0.01m, InputTokens: 1, OutputTokens: 1,
            FilesModified: Array.Empty<string>(), ExitCode: 0,
            AssistantSessionId: null);

        var result = await RunLoop(stubs, new IterativeLoopInput(
            SessionId: "s-min-force",
            Prompt: "build x",
            AiAssistant: "claude", Model: null, ModelPower: 2,
            MinIterations: 3, MaxIterations: 10));

        result.ExitReason.Should().Be("done");
        result.IterationsRun.Should().Be(3);
    }

    [Fact]
    public async Task StopsAtMax_WhenMarkerNeverFires()
    {
        var stubs = new Stubs();
        stubs.RunResponder = _ => new RunCliAgentOutput(
            Response: "- [ ] stuck\nCompletion: false",
            StructuredOutputJson: null, Success: true,
            CostUsd: 0.01m, InputTokens: 1, OutputTokens: 1,
            FilesModified: Array.Empty<string>(), ExitCode: 0,
            AssistantSessionId: null);

        var result = await RunLoop(stubs, new IterativeLoopInput(
            SessionId: "s-max",
            Prompt: "build x",
            AiAssistant: "claude", Model: null, ModelPower: 2,
            MinIterations: 1, MaxIterations: 3));

        result.ExitReason.Should().Be("max-iterations");
        result.IterationsRun.Should().Be(3);
        result.DoneMarkerObserved.Should().BeFalse();
    }

    [Fact]
    public async Task BudgetCap_StopsLoop()
    {
        var stubs = new Stubs();
        stubs.RunResponder = _ => new RunCliAgentOutput(
            Response: "- [ ] stuck\nCompletion: false",
            StructuredOutputJson: null, Success: true,
            CostUsd: 0.60m, InputTokens: 0, OutputTokens: 0,
            FilesModified: Array.Empty<string>(), ExitCode: 0,
            AssistantSessionId: null);

        var result = await RunLoop(stubs, new IterativeLoopInput(
            SessionId: "s-budget",
            Prompt: "build x",
            AiAssistant: "claude", Model: null, ModelPower: 2,
            MinIterations: 1, MaxIterations: 10,
            MaxBudgetUsd: 1.0m));

        result.ExitReason.Should().Be("budget");
        result.IterationsRun.Should().Be(2);   // 0.60 + 0.60 = 1.20 ≥ 1.0
    }

    [Fact]
    public async Task ReusesContainer_WhenExistingContainerIdProvided()
    {
        var stubs = new Stubs();
        stubs.RunResponder = _ => new RunCliAgentOutput(
            Response: BuildDoneReport("task"),
            StructuredOutputJson: null, Success: true,
            CostUsd: 0.01m, InputTokens: 1, OutputTokens: 1,
            FilesModified: Array.Empty<string>(), ExitCode: 0,
            AssistantSessionId: null);

        var result = await RunLoop(stubs, new IterativeLoopInput(
            SessionId: "s-reuse",
            Prompt: "build x",
            AiAssistant: "claude", Model: null, ModelPower: 2,
            MinIterations: 1, MaxIterations: 3,
            ExistingContainerId: "parent-container-123"));

        result.ExitReason.Should().Be("done");
        stubs.SpawnCalls.Should().Be(0);
        stubs.DestroyCalls.Should().Be(0);
        stubs.LastContainerIdSeenByRun.Should().Be("parent-container-123");
    }

    [Fact]
    public async Task ThreadsAssistantSessionId_AcrossIterations()
    {
        var stubs = new Stubs();
        var iter = 0;
        stubs.RunResponder = inp =>
        {
            iter++;
            // iter 1 returns a session id; iter 2 should send it back
            if (iter == 1)
                stubs.FirstCallAssistantSessionId = inp.AssistantSessionId;
            else
                stubs.SecondCallAssistantSessionId = inp.AssistantSessionId;

            return new RunCliAgentOutput(
                Response: iter >= 2 ? BuildDoneReport("task") : "- [ ] x\nCompletion: false",
                StructuredOutputJson: null, Success: true,
                CostUsd: 0.01m, InputTokens: 1, OutputTokens: 1,
                FilesModified: Array.Empty<string>(), ExitCode: 0,
                AssistantSessionId: "claude-session-42");
        };

        await RunLoop(stubs, new IterativeLoopInput(
            SessionId: "s-thread",
            Prompt: "build",
            AiAssistant: "claude", Model: null, ModelPower: 2,
            MinIterations: 1, MaxIterations: 5));

        stubs.FirstCallAssistantSessionId.Should().BeNull();
        stubs.SecondCallAssistantSessionId.Should().Be("claude-session-42");
    }

    [Fact]
    public async Task MarkerStrategy_UsesSimpleStringMatch()
    {
        var stubs = new Stubs();
        var first = true;
        stubs.RunResponder = _ =>
        {
            var text = first ? "still working on it" : "all done\n[DONE]";
            first = false;
            return new RunCliAgentOutput(
                Response: text,
                StructuredOutputJson: null, Success: true,
                CostUsd: 0.01m, InputTokens: 1, OutputTokens: 1,
                FilesModified: Array.Empty<string>(), ExitCode: 0,
                AssistantSessionId: null);
        };

        var result = await RunLoop(stubs, new IterativeLoopInput(
            SessionId: "s-marker",
            Prompt: "x",
            AiAssistant: "claude", Model: null, ModelPower: 2,
            MinIterations: 1, MaxIterations: 5,
            CompletionStrategy: CompletionStrategy.Marker));

        result.ExitReason.Should().Be("done");
        result.IterationsRun.Should().Be(2);
    }

    [Fact]
    public async Task ClassifierStrategy_DelegatesToClassifyAsync()
    {
        var stubs = new Stubs();
        stubs.RunResponder = _ => new RunCliAgentOutput(
            Response: "freeform reply that doesn't contain the marker",
            StructuredOutputJson: null, Success: true,
            CostUsd: 0.01m, InputTokens: 1, OutputTokens: 1,
            FilesModified: Array.Empty<string>(), ExitCode: 0,
            AssistantSessionId: null);
        stubs.ClassifierResponder = _ =>
            new ClassifierOutput(Result: true, Confidence: 0.9m, Rationale: "looks done");

        var result = await RunLoop(stubs, new IterativeLoopInput(
            SessionId: "s-classify",
            Prompt: "x",
            AiAssistant: "claude", Model: null, ModelPower: 2,
            MinIterations: 1, MaxIterations: 5,
            CompletionStrategy: CompletionStrategy.Classifier));

        result.ExitReason.Should().Be("done");
        result.IterationsRun.Should().Be(1);
        stubs.ClassifyCalls.Should().Be(1);
    }

    [Fact]
    public async Task InvalidMaxIterations_Throws()
    {
        var stubs = new Stubs();
        var act = async () => await RunLoop(stubs, new IterativeLoopInput(
            SessionId: "s-bad",
            Prompt: "x",
            AiAssistant: "claude", Model: null, ModelPower: 2,
            MinIterations: 5, MaxIterations: 0));

        await act.Should().ThrowAsync<Exception>();
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private static string BuildDoneReport(string taskName) => $"""
        ### Task Status
        - [x] {taskName} — done

        ### Current Work
        wrapped up

        ### Blockers
        None

        ### Completion
        Completion: true

        [DONE]
        """;

    private async Task<IterativeLoopOutput> RunLoop(Stubs stubs, IterativeLoopInput input)
    {
        using var worker = new TemporalWorker(
            _env.Client,
            new TemporalWorkerOptions($"test-il-{Guid.NewGuid():N}")
                .AddAllActivities(stubs)
                .AddAllActivities(new StageActivityStubs())
                .AddWorkflow<IterativeLoopWorkflow>());

        return await worker.ExecuteAsync(async () =>
        {
            var handle = await _env.Client.StartWorkflowAsync(
                (IterativeLoopWorkflow w) => w.RunAsync(input),
                new(id: $"il-{Guid.NewGuid():N}", taskQueue: worker.Options.TaskQueue!));
            return await handle.GetResultAsync();
        });
    }

    public class Stubs
    {
        public int SpawnCalls, DestroyCalls, ClassifyCalls;
        public string? LastContainerIdSeenByRun;
        public string? FirstCallAssistantSessionId;
        public string? SecondCallAssistantSessionId;

        public Func<SpawnContainerInput, SpawnContainerOutput> SpawnResponder { get; set; } =
            i => new SpawnContainerOutput($"stub-{i.SessionId}", null);

        public Func<RunCliAgentInput, RunCliAgentOutput> RunResponder { get; set; } =
            _ => new RunCliAgentOutput(
                "", null, true, 0m, 0, 0, Array.Empty<string>(), 0, null);

        public Func<ClassifierInput, ClassifierOutput> ClassifierResponder { get; set; } =
            _ => new ClassifierOutput(false, 0.5m, "");

        [Activity]
        public Task<SpawnContainerOutput> SpawnAsync(SpawnContainerInput i)
        {
            Interlocked.Increment(ref SpawnCalls);
            return Task.FromResult(SpawnResponder(i));
        }

        [Activity]
        public Task DestroyAsync(DestroyInput i)
        {
            Interlocked.Increment(ref DestroyCalls);
            return Task.CompletedTask;
        }

        [Activity]
        public Task<RunCliAgentOutput> RunCliAgentAsync(RunCliAgentInput i)
        {
            LastContainerIdSeenByRun = i.ContainerId;
            return Task.FromResult(RunResponder(i));
        }

        [Activity]
        public Task<ClassifierOutput> ClassifyAsync(ClassifierInput i)
        {
            Interlocked.Increment(ref ClassifyCalls);
            return Task.FromResult(ClassifierResponder(i));
        }
    }
}
