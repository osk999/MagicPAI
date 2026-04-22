using FluentAssertions;
using Temporalio.Activities;
using Temporalio.Testing;
using Temporalio.Worker;
using MagicPAI.Activities.Contracts;
using MagicPAI.Server.Workflows;
using MagicPAI.Workflows.Contracts;

namespace MagicPAI.Tests.Workflows;

/// <summary>
/// Integration tests for the Temporal <see cref="FullOrchestrateWorkflow"/> —
/// the central orchestrator. Two happy-path scenarios are covered:
/// <list type="bullet">
///   <item>website-path: classifier returns true so the website-audit child workflow runs.</item>
///   <item>simple-path: classifier returns false, triage says not complex, so SimpleAgent runs.</item>
/// </list>
/// The complex-path scenario would require stubbing Architect + N
/// ComplexTaskWorker children; covered at integration level via
/// <see cref="OrchestrateComplexPathWorkflowTests"/>.
/// See temporal.md §8.6.
/// </summary>
[Trait("Category", "Integration")]
public class FullOrchestrateWorkflowTests : IAsyncLifetime
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
    /// Simple-path branch — website classifier returns false, triage returns
    /// complexity below threshold so SimpleAgent runs. We register both
    /// FullOrchestrate and SimpleAgent on the same worker and stub every shared
    /// activity. Asserts PipelineUsed="simple" and container destroy happens.
    /// </summary>
    /// <summary>
    /// Fix #159 regression guard: `ComplexityThreshold` on
    /// <see cref="FullOrchestrateInput"/> must be forwarded to the
    /// <see cref="TriageInput.ComplexityThreshold"/> so callers can tune
    /// how aggressively prompts route to the complex-path branch.
    /// </summary>
    [Fact]
    public async Task Fix159_ComplexityThreshold_IsPassedToTriage()
    {
        int? observedThreshold = null;
        var stubs = new FullOrchestrateStubs
        {
            ClassifyResponder = _ => new ClassifierOutput(
                Result: false, Confidence: 0.9m, Rationale: "not a website"),
            TriageResponder = i =>
            {
                observedThreshold = i.ComplexityThreshold;
                return new TriageOutput(
                    Complexity: 3,
                    Category: "code_gen",
                    RecommendedModel: "claude-sonnet",
                    RecommendedModelPower: 2,
                    NeedsDecomposition: false,
                    IsComplex: false);
            },
        };

        using var worker = new TemporalWorker(
            _env.Client,
            new TemporalWorkerOptions($"test-fo-thresh-{Guid.NewGuid():N}")
                .AddAllActivities(stubs)
                .AddWorkflow<FullOrchestrateWorkflow>()
                .AddWorkflow<ResearchPipelineWorkflow>()
                .AddWorkflow<IterativeLoopWorkflow>()
                .AddWorkflow<SimpleAgentWorkflow>());

        await worker.ExecuteAsync(async () =>
        {
            var input = new FullOrchestrateInput(
                SessionId: "fo-thresh",
                Prompt: "do a thing",
                WorkspacePath: "/workspace",
                AiAssistant: "claude",
                Model: null,
                ModelPower: 2,
                EnableGui: false,
                MaxCoverageIterations: 1,
                RequireTriageApproval: false,
                GateApprovalTimeoutHours: 24,
                ComplexityThreshold: 3);  // <— explicit lower threshold

            var handle = await _env.Client.StartWorkflowAsync(
                (FullOrchestrateWorkflow wf) => wf.RunAsync(input),
                new(id: $"fo-thresh-{Guid.NewGuid():N}",
                    taskQueue: worker.Options.TaskQueue!));

            await handle.GetResultAsync();

            // The crucial assertion — triage received our ComplexityThreshold.
            observedThreshold.Should().Be(3);
        });
    }

    [Fact]
    public async Task Completes_SimplePath_WhenNotWebsite_AndNotComplex()
    {
        var stubs = new FullOrchestrateStubs
        {
            ClassifyResponder = _ => new ClassifierOutput(
                Result: false, Confidence: 0.9m, Rationale: "not a website task"),
            TriageResponder = _ => new TriageOutput(
                Complexity: 3,
                Category: "code_gen",
                RecommendedModel: "claude-sonnet",
                RecommendedModelPower: 2,
                NeedsDecomposition: false,
                IsComplex: false),
        };

        using var worker = new TemporalWorker(
            _env.Client,
            new TemporalWorkerOptions($"test-fo-simple-{Guid.NewGuid():N}")
                .AddAllActivities(stubs)
                .AddWorkflow<FullOrchestrateWorkflow>()
                .AddWorkflow<ResearchPipelineWorkflow>()
                .AddWorkflow<IterativeLoopWorkflow>()
                .AddWorkflow<SimpleAgentWorkflow>());

        await worker.ExecuteAsync(async () =>
        {
            var input = new FullOrchestrateInput(
                SessionId: "fo-simple",
                Prompt: "add a helper function",
                WorkspacePath: "/workspace",
                AiAssistant: "claude",
                Model: null,
                ModelPower: 2,
                EnableGui: false);

            var handle = await _env.Client.StartWorkflowAsync(
                (FullOrchestrateWorkflow wf) => wf.RunAsync(input),
                new(id: $"fo-simple-{Guid.NewGuid():N}",
                    taskQueue: worker.Options.TaskQueue!));

            var result = await handle.GetResultAsync();

            result.PipelineUsed.Should().Be("simple");
            // SimpleAgent happy path returns "stub-response" from our RunCliAgent stub.
            result.FinalResponse.Should().Be("stub-response");
            result.TotalCostUsd.Should().BeGreaterThan(0m);

            stubs.ClassifyCallCount.Should().Be(1);
            // Research is now an IterativeLoop child (not a single
            // ResearchPromptAsync activity). The old ResearchPromptAsync stub
            // is no longer invoked; research is driven through RunCliAgentAsync
            // inside IterativeLoopWorkflow instead.
            stubs.ResearchCallCount.Should().Be(0);
            stubs.TriageCallCount.Should().Be(1);
            // Only the parent container is spawned + destroyed. The SimpleAgent
            // child reuses the parent's container via ExistingContainerId to
            // avoid a double-spawn collision on noVNC port 6080 (the bug fix).
            stubs.SpawnCallCount.Should().Be(1);
            stubs.DestroyedContainerIds.Should().ContainSingle();

            // Capture the simple-path branch as the canonical happy-path fixture.
            // (The spec allows any of simple / website / complex; simple is the
            // cheapest path to replay deterministically.)
            await ReplayFixtureHelper.CaptureIfMissingAsync(
                handle, "full-orchestrate", "happy-path-v1.json");
        });
    }

    /// <summary>
    /// Website-path branch — classifier returns true so the website-audit loop
    /// workflow runs. Asserts PipelineUsed="website-audit" and the summary
    /// carries the aggregated section reports.
    /// </summary>
    [Fact]
    public async Task Completes_WebsitePath_WhenClassifierReturnsTrue()
    {
        var stubs = new FullOrchestrateStubs
        {
            ClassifyResponder = _ => new ClassifierOutput(
                Result: true, Confidence: 0.95m, Rationale: "homepage audit"),
        };

        using var worker = new TemporalWorker(
            _env.Client,
            new TemporalWorkerOptions($"test-fo-web-{Guid.NewGuid():N}")
                .AddAllActivities(stubs)
                .AddWorkflow<FullOrchestrateWorkflow>()
                .AddWorkflow<ResearchPipelineWorkflow>()
                .AddWorkflow<IterativeLoopWorkflow>()
                .AddWorkflow<WebsiteAuditLoopWorkflow>()
                .AddWorkflow<WebsiteAuditCoreWorkflow>());

        await worker.ExecuteAsync(async () =>
        {
            var input = new FullOrchestrateInput(
                SessionId: "fo-web",
                Prompt: "audit my homepage",
                WorkspacePath: "/workspace",
                AiAssistant: "claude",
                Model: null,
                ModelPower: 2,
                EnableGui: false);

            var handle = await _env.Client.StartWorkflowAsync(
                (FullOrchestrateWorkflow wf) => wf.RunAsync(input),
                new(id: $"fo-web-{Guid.NewGuid():N}",
                    taskQueue: worker.Options.TaskQueue!));

            var result = await handle.GetResultAsync();

            result.PipelineUsed.Should().Be("website-audit");
            // Default section set is homepage/navigation/forms/checkout/footer;
            // each returns a 1-issue stub payload. Summary concatenates them.
            result.FinalResponse.Should().Contain("## homepage");
            result.FinalResponse.Should().Contain("## navigation");
            stubs.DestroyedContainerIds.Should().ContainSingle();   // only parent's
        });
    }

    /// <summary>
    /// HITL approval gate — when <see cref="FullOrchestrateInput.RequireTriageApproval"/>
    /// is true the workflow must park after triage until <c>ApproveGate</c> /
    /// <c>RejectGate</c> arrives. We exercise three paths:
    /// <list type="bullet">
    ///   <item>approve → workflow continues, <c>simple</c> branch completes.</item>
    ///   <item>reject → workflow returns <c>PipelineUsed="rejected"</c> with the supplied reason.</item>
    ///   <item>timeout → workflow returns <c>PipelineUsed="rejected"</c> with reason <c>"timeout"</c>.</item>
    /// </list>
    /// All three rely on <see cref="WorkflowEnvironment.StartTimeSkippingAsync"/>
    /// so the 24-hour timeout collapses to milliseconds in the timeout test.
    /// </summary>
    [Fact]
    public async Task ApprovalGate_Blocks_UntilSignal()
    {
        // ── Path 1: approve ──────────────────────────────────────────────
        await RunGateScenario(
            scenarioName: "approve",
            requireApproval: true,
            timeoutHours: 24,
            signal: async handle =>
            {
                // Wait for the workflow to reach the gate (AwaitingApproval=true)
                // before signalling, otherwise StartTimeSkipping may complete
                // before the signal arrives.
                await WaitForAwaitingApprovalAsync(handle);
                await handle.SignalAsync<FullOrchestrateWorkflow>(
                    wf => wf.ApproveGateAsync("test-user"));
            },
            assert: result =>
            {
                result.PipelineUsed.Should().Be("simple");
                result.FinalResponse.Should().Be("stub-response");
            });

        // ── Path 2: reject ───────────────────────────────────────────────
        await RunGateScenario(
            scenarioName: "reject",
            requireApproval: true,
            timeoutHours: 24,
            signal: async handle =>
            {
                await WaitForAwaitingApprovalAsync(handle);
                await handle.SignalAsync<FullOrchestrateWorkflow>(
                    wf => wf.RejectGateAsync("not safe"));
            },
            assert: result =>
            {
                result.PipelineUsed.Should().Be("rejected");
                result.FinalResponse.Should().Contain("not safe");
                // Research ran BEFORE the HITL gate so cost accumulates even
                // on rejection. Just verify it isn't negative / corrupt.
                result.TotalCostUsd.Should().BeGreaterThanOrEqualTo(0m);
            });

        // ── Path 3: timeout ──────────────────────────────────────────────
        // 1-hour timeout + no signal. Time-skipping fast-forwards past it.
        await RunGateScenario(
            scenarioName: "timeout",
            requireApproval: true,
            timeoutHours: 1,
            signal: _ => Task.CompletedTask,
            assert: result =>
            {
                result.PipelineUsed.Should().Be("rejected");
                result.FinalResponse.Should().Contain("timeout");
            });
    }

    private async Task RunGateScenario(
        string scenarioName,
        bool requireApproval,
        int timeoutHours,
        Func<Temporalio.Client.WorkflowHandle<FullOrchestrateWorkflow, FullOrchestrateOutput>, Task> signal,
        Action<FullOrchestrateOutput> assert)
    {
        var stubs = new FullOrchestrateStubs
        {
            ClassifyResponder = _ => new ClassifierOutput(
                Result: false, Confidence: 0.9m, Rationale: "not a website"),
            TriageResponder = _ => new TriageOutput(
                Complexity: 3,
                Category: "code_gen",
                RecommendedModel: "claude-sonnet",
                RecommendedModelPower: 2,
                NeedsDecomposition: false,
                IsComplex: false),
        };

        using var worker = new TemporalWorker(
            _env.Client,
            new TemporalWorkerOptions($"test-fo-gate-{scenarioName}-{Guid.NewGuid():N}")
                .AddAllActivities(stubs)
                .AddWorkflow<FullOrchestrateWorkflow>()
                .AddWorkflow<ResearchPipelineWorkflow>()
                .AddWorkflow<IterativeLoopWorkflow>()
                .AddWorkflow<SimpleAgentWorkflow>());

        await worker.ExecuteAsync(async () =>
        {
            var input = new FullOrchestrateInput(
                SessionId: $"fo-gate-{scenarioName}",
                Prompt: "do the thing",
                WorkspacePath: "/workspace",
                AiAssistant: "claude",
                Model: null,
                ModelPower: 2,
                EnableGui: false,
                MaxCoverageIterations: 2,
                RequireTriageApproval: requireApproval,
                GateApprovalTimeoutHours: timeoutHours);

            var handle = await _env.Client.StartWorkflowAsync(
                (FullOrchestrateWorkflow wf) => wf.RunAsync(input),
                new(id: $"fo-gate-{scenarioName}-{Guid.NewGuid():N}",
                    taskQueue: worker.Options.TaskQueue!));

            await signal(handle);
            var result = await handle.GetResultAsync();
            assert(result);

            // Container is destroyed on every exit path, including rejection.
            stubs.DestroyedContainerIds.Should().ContainSingle(
                because: $"scenario {scenarioName} must still run the cleanup `finally` block");
        });
    }

    /// <summary>
    /// Polls the AwaitingApproval query until it flips true. Time-skipping
    /// environments don't sleep wall-clock seconds for short delays, so this
    /// returns quickly under normal conditions; we cap at ~5s as a safety net.
    /// </summary>
    private static async Task WaitForAwaitingApprovalAsync(
        Temporalio.Client.WorkflowHandle<FullOrchestrateWorkflow, FullOrchestrateOutput> handle)
    {
        for (var i = 0; i < 100; i++)
        {
            try
            {
                var awaiting = await handle.QueryAsync(wf => wf.AwaitingApproval);
                if (awaiting) return;
            }
            catch (Temporalio.Exceptions.WorkflowQueryRejectedException)
            {
                // Workflow may not yet be ready to answer queries — retry.
            }
            await Task.Delay(50);
        }
        throw new TimeoutException("Workflow never entered awaiting-gate-approval state.");
    }

    /// <summary>
    /// Stub bag covering every activity the central orchestrator + simple-path
    /// + website-path + verify-repair touch. Activity-name rule: method name
    /// minus "Async" suffix.
    /// </summary>
    public class FullOrchestrateStubs
    {
        public Func<SpawnContainerInput, SpawnContainerOutput> SpawnResponder { get; set; } =
            i => new SpawnContainerOutput($"stub-container-{i.SessionId}", null);

        public Func<ClassifierInput, ClassifierOutput> ClassifyResponder { get; set; } =
            _ => new ClassifierOutput(Result: false, Confidence: 0.9m, Rationale: "default");

        public Func<ResearchPromptInput, ResearchPromptOutput> ResearchResponder { get; set; } =
            i => new ResearchPromptOutput(
                EnhancedPrompt: $"researched: {i.Prompt}",
                CodebaseAnalysis: "stub analysis",
                ResearchContext: "stub context",
                Rationale: "stub rationale");

        public Func<TriageInput, TriageOutput> TriageResponder { get; set; } =
            _ => new TriageOutput(
                Complexity: 3,
                Category: "code_gen",
                RecommendedModel: "claude-sonnet",
                RecommendedModelPower: 2,
                NeedsDecomposition: false,
                IsComplex: false);

        public Func<RunCliAgentInput, RunCliAgentOutput> RunResponder { get; set; } =
            i =>
            {
                // Research-loop iterations carry the multi-pass research prompt;
                // return a complete structured-progress report so the inner
                // IterativeLoopWorkflow exits on its first valid pass (subject
                // to MinIterations, which is 3 for research).
                if (i.Prompt.Contains("MULTI-PASS deep research"))
                {
                    return new RunCliAgentOutput(
                        Response: StubResearchResponse,
                        StructuredOutputJson: null,
                        Success: true,
                        CostUsd: 0.05m,
                        InputTokens: 1, OutputTokens: 1,
                        FilesModified: Array.Empty<string>(),
                        ExitCode: 0,
                        AssistantSessionId: "stub-research");
                }

                // Website-audit child supplies a structured output schema; emit a
                // matching JSON payload so the core workflow's parser succeeds.
                string? structured = null;
                var response = "stub-response";
                if (!string.IsNullOrWhiteSpace(i.StructuredOutputSchema))
                {
                    var sectionTag = ExtractSectionTag(i.Prompt);
                    structured = $"{{\"report\":\"Audit for {sectionTag}\",\"issueCount\":1}}";
                    response = structured;
                }
                return new RunCliAgentOutput(
                    Response: response,
                    StructuredOutputJson: structured,
                    Success: true,
                    CostUsd: 0.25m,
                    InputTokens: 100,
                    OutputTokens: 200,
                    FilesModified: new[] { "foo.cs" },
                    ExitCode: 0,
                    AssistantSessionId: "stub");
            };

        private const string StubResearchResponse = """
            ## Rewritten Task
            stub rewritten task body.

            ## Codebase Analysis
            stub codebase analysis.

            ## Research Context
            stub research context.

            ## Rationale
            stub rationale.

            ### Task Status
            - [x] A1
            - [x] A2
            - [x] A3
            - [x] B1
            - [x] B2
            - [x] B3
            - [x] C1
            - [x] C2
            - [x] C3
            - [x] C4
            - [x] D1
            - [x] D2

            ### Completion
            Completion: true

            [DONE]
            """;

        public Func<VerifyInput, VerifyOutput> VerifyResponder { get; set; } =
            _ => new VerifyOutput(
                AllPassed: true,
                FailedGates: Array.Empty<string>(),
                GateResultsJson: "[]");

        public Func<CoverageInput, CoverageOutput> CoverageResponder { get; set; } =
            i => new CoverageOutput(
                AllMet: true,
                GapPrompt: "",
                CoverageReportJson: "{\"gaps\":[]}",
                Iteration: i.CurrentIteration);

        public List<string> DestroyedContainerIds { get; } = new();

        private int _classifyCalls;
        private int _researchCalls;
        private int _triageCalls;
        private int _spawnCalls;
        public int ClassifyCallCount => _classifyCalls;
        public int ResearchCallCount => _researchCalls;
        public int TriageCallCount => _triageCalls;
        public int SpawnCallCount => _spawnCalls;

        [Activity]
        public Task<SpawnContainerOutput> SpawnAsync(SpawnContainerInput i)
        {
            Interlocked.Increment(ref _spawnCalls);
            return Task.FromResult(SpawnResponder(i));
        }

        [Activity]
        public Task DestroyAsync(DestroyInput i)
        {
            lock (DestroyedContainerIds) { DestroyedContainerIds.Add(i.ContainerId); }
            return Task.CompletedTask;
        }

        // ResearchPipeline's fallback `cat /workspace/research.md` path.
        public Func<ExecInput, ExecOutput> ExecResponder { get; set; } =
            _ => new ExecOutput(ExitCode: 1, Output: "", Error: "no such file");

        [Activity]
        public Task<ExecOutput> ExecAsync(ExecInput i) =>
            Task.FromResult(ExecResponder(i));

        [Activity]
        public Task<ClassifierOutput> ClassifyAsync(ClassifierInput i)
        {
            Interlocked.Increment(ref _classifyCalls);
            return Task.FromResult(ClassifyResponder(i));
        }

        // ClassifyWebsiteTask in the real AiActivities delegates to Classify; the
        // workflow calls ClassifyWebsiteTask directly so we register it as the
        // top-level seam.
        [Activity]
        public Task<WebsiteClassifyOutput> ClassifyWebsiteTaskAsync(WebsiteClassifyInput i)
        {
            Interlocked.Increment(ref _classifyCalls);
            var classifier = new ClassifierInput(
                Prompt: i.Prompt,
                ClassificationQuestion: "Is this website?",
                ContainerId: i.ContainerId,
                ModelPower: 3,
                AiAssistant: i.AiAssistant,
                SessionId: i.SessionId);
            var r = ClassifyResponder(classifier);
            return Task.FromResult(new WebsiteClassifyOutput(
                IsWebsiteTask: r.Result,
                Confidence: r.Confidence,
                Rationale: r.Rationale));
        }

        [Activity]
        public Task<ResearchPromptOutput> ResearchPromptAsync(ResearchPromptInput i)
        {
            Interlocked.Increment(ref _researchCalls);
            return Task.FromResult(ResearchResponder(i));
        }

        [Activity]
        public Task<TriageOutput> TriageAsync(TriageInput i)
        {
            Interlocked.Increment(ref _triageCalls);
            return Task.FromResult(TriageResponder(i));
        }

        [Activity]
        public Task<RunCliAgentOutput> RunCliAgentAsync(RunCliAgentInput i) =>
            Task.FromResult(RunResponder(i));

        [Activity]
        public Task<VerifyOutput> RunGatesAsync(VerifyInput i) =>
            Task.FromResult(VerifyResponder(i));

        [Activity]
        public Task<CoverageOutput> GradeCoverageAsync(CoverageInput i) =>
            Task.FromResult(CoverageResponder(i));

        // Extract the section id from the audit-core prompt. The core workflow
        // formats each section prompt as "...\nSection: <id>\n...".
        private static string ExtractSectionTag(string prompt)
        {
            const string marker = "Section: ";
            var idx = prompt.IndexOf(marker, StringComparison.Ordinal);
            if (idx < 0) return "unknown";
            var rest = prompt[(idx + marker.Length)..];
            var end = rest.IndexOf('\n');
            return end < 0 ? rest.Trim() : rest[..end].Trim();
        }
    }
}
