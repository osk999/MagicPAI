using FluentAssertions;
using MagicPAI.Activities.AI;
using MagicPAI.Activities.Contracts;
using MagicPAI.Core.Config;
using MagicPAI.Core.Models;
using MagicPAI.Core.Services;
using MagicPAI.Core.Services.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Temporalio.Testing;

namespace MagicPAI.Tests.Activities;

/// <summary>
/// Unit tests for <see cref="AiActivities"/> (Temporal activity group) — Day 4 methods:
/// <c>TriageAsync</c>, <c>ClassifyAsync</c>, <c>RouteModelAsync</c>, <c>EnhancePromptAsync</c>.
/// Uses <see cref="ActivityEnvironment"/> from the Temporal SDK plus mocked
/// <see cref="IContainerManager"/> and <see cref="ICliAgentFactory"/>. See temporal.md §15.3.
/// </summary>
[Trait("Category", "Unit")]
public class AiActivitiesTests
{
    private static Mock<ICliAgentRunner> MakeCodexRunner()
    {
        var m = new Mock<ICliAgentRunner>(MockBehavior.Loose);
        m.SetupGet(r => r.AgentName).Returns("codex");
        m.SetupGet(r => r.DefaultModel).Returns("gpt-5.3-codex");
        m.SetupGet(r => r.AvailableModels)
         .Returns(new[] { "gpt-5.4", "gpt-5.3-codex", "gpt-5.4-mini" });
        m.SetupGet(r => r.SupportsNativeSchema).Returns(true);
        m.Setup(r => r.BuildExecutionPlan(It.IsAny<AgentRequest>()))
         .Returns(new CliAgentExecutionPlan(
             new ContainerExecRequest("codex", new[] { "--noop" }, "/workspace")));
        return m;
    }

    private static Mock<ICliAgentRunner> MakeGeminiRunner()
    {
        var m = new Mock<ICliAgentRunner>(MockBehavior.Loose);
        m.SetupGet(r => r.AgentName).Returns("gemini");
        m.SetupGet(r => r.DefaultModel).Returns("gemini-3-flash");
        m.SetupGet(r => r.AvailableModels)
         .Returns(new[] { "gemini-3.1-pro-preview", "gemini-3-flash", "gemini-3.1-flash-lite-preview" });
        m.SetupGet(r => r.SupportsNativeSchema).Returns(false);
        m.Setup(r => r.BuildExecutionPlan(It.IsAny<AgentRequest>()))
         .Returns(new CliAgentExecutionPlan(
             new ContainerExecRequest("gemini", new[] { "--noop" }, "/workspace")));
        return m;
    }

    private static AiActivities BuildSut(
        Mock<IContainerManager>? docker = null,
        Mock<ICliAgentFactory>? factory = null,
        Mock<ICliAgentRunner>? runner = null,
        Mock<ISessionStreamSink>? sink = null,
        MagicPaiConfig? config = null)
    {
        config ??= new MagicPaiConfig { ExecutionBackend = "docker" };

        // Default runner returns a pass-through ParseResponse so tests that don't
        // need custom parsing can just focus on the container exec output.
        // NOTE: AgentName defaults to "claude"; RouteModel tests that exercise a
        // different agent pass an explicit `runner` mock with the right AgentName.
        runner ??= new Mock<ICliAgentRunner>(MockBehavior.Loose);
        runner.SetupGet(r => r.AgentName).Returns("claude");
        runner.SetupGet(r => r.DefaultModel).Returns("sonnet");
        runner.SetupGet(r => r.AvailableModels)
              .Returns(new[] { "haiku", "sonnet", "opus" });
        runner.SetupGet(r => r.SupportsNativeSchema).Returns(true);
        runner.Setup(r => r.BuildExecutionPlan(It.IsAny<AgentRequest>()))
              .Returns(new CliAgentExecutionPlan(
                  new ContainerExecRequest("claude", new[] { "--noop" }, "/workspace")));
        runner.Setup(r => r.ParseResponse(It.IsAny<string>()))
              .Returns<string>(raw => new CliAgentResponse(
                  Success: true,
                  Output: raw,
                  CostUsd: 0m,
                  FilesModified: Array.Empty<string>(),
                  InputTokens: 0,
                  OutputTokens: 0,
                  SessionId: null,
                  StructuredOutputJson: raw));

        // Factory dispatches on the normalized assistant name: claude/codex/gemini.
        // Each runner has the matching AgentName so ResolveModelForPower returns
        // the right provider's default model.
        factory ??= new Mock<ICliAgentFactory>(MockBehavior.Loose);
        factory.Setup(f => f.Create("claude")).Returns(runner.Object);
        factory.Setup(f => f.Create("codex")).Returns(MakeCodexRunner().Object);
        factory.Setup(f => f.Create("gemini")).Returns(MakeGeminiRunner().Object);
        factory.SetupGet(f => f.AvailableAgents).Returns(new[] { "claude", "codex", "gemini" });

        docker ??= new Mock<IContainerManager>(MockBehavior.Loose);
        sink ??= new Mock<ISessionStreamSink>(MockBehavior.Loose);

        // AuthRecoveryService is concrete; construct with config. It is only
        // exercised on a detected auth error, which none of these tests trigger.
        var auth = new AuthRecoveryService(config);

        return new AiActivities(
            factory: factory.Object,
            docker: docker.Object,
            sink: sink.Object,
            auth: auth,
            config: config,
            log: NullLogger<AiActivities>.Instance);
    }

    // ── TriageAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task TriageAsync_ParsesResponse_ReturnsComplexity()
    {
        // Arrange — the runner's ParseResponse surfaces this JSON as the payload.
        var triageJson = """
            {"complexity":8,"category":"refactor","needs_decomposition":true,"recommended_model_power":1}
            """;
        var docker = new Mock<IContainerManager>(MockBehavior.Strict);
        docker.Setup(d => d.ExecAsync(
                   It.IsAny<string>(),
                   It.IsAny<ContainerExecRequest>(),
                   It.IsAny<CancellationToken>()))
              .ReturnsAsync(new ExecResult(0, triageJson, ""));

        var sut = BuildSut(docker: docker);
        var input = new TriageInput(
            Prompt: "Refactor the auth subsystem across four modules",
            ContainerId: "ctr-42",
            ClassificationInstructions: null,
            AiAssistant: "claude",
            ComplexityThreshold: 7);

        // Act
        var env = new ActivityEnvironment();
        var output = await env.RunAsync(() => sut.TriageAsync(input));

        // Assert
        output.Complexity.Should().Be(8);
        output.Category.Should().Be("refactor");
        output.RecommendedModelPower.Should().Be(1);
        output.RecommendedModel.Should().Be("opus"); // power=1 → claude opus
        output.NeedsDecomposition.Should().BeTrue();
        output.IsComplex.Should().BeTrue();
    }

    [Fact]
    public async Task TriageAsync_FallsBack_WhenJsonInvalid()
    {
        // Arrange — exit 0 but non-JSON body → internal JSON parser falls through to
        // { 5, code_gen, 2, false }. IsComplex becomes false against threshold 7.
        var docker = new Mock<IContainerManager>(MockBehavior.Strict);
        docker.Setup(d => d.ExecAsync(
                   It.IsAny<string>(),
                   It.IsAny<ContainerExecRequest>(),
                   It.IsAny<CancellationToken>()))
              .ReturnsAsync(new ExecResult(0, "I think it is 7 out of 10", ""));

        var sut = BuildSut(docker: docker);
        var input = new TriageInput(
            Prompt: "Add a logger",
            ContainerId: "ctr-42",
            ClassificationInstructions: null,
            AiAssistant: "claude",
            ComplexityThreshold: 7);

        // Act
        var env = new ActivityEnvironment();
        var output = await env.RunAsync(() => sut.TriageAsync(input));

        // Assert
        output.Complexity.Should().Be(5);
        output.Category.Should().Be("code_gen");
        output.RecommendedModelPower.Should().Be(2);
        output.RecommendedModel.Should().Be("sonnet");
        output.NeedsDecomposition.Should().BeFalse();
        output.IsComplex.Should().BeFalse();
    }

    [Fact]
    public async Task TriageAsync_Throws_WhenContainerIdMissing()
    {
        var sut = BuildSut();
        var input = new TriageInput(
            Prompt: "any",
            ContainerId: "",
            ClassificationInstructions: null,
            AiAssistant: "claude");

        var env = new ActivityEnvironment();
        Func<Task> act = async () => await env.RunAsync(() => sut.TriageAsync(input));

        await act.Should()
                 .ThrowAsync<Temporalio.Exceptions.ApplicationFailureException>()
                 .Where(e => e.ErrorType == "ConfigError");
    }

    // ── ClassifyAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task ClassifyAsync_ReturnsTrue_WhenAgentAgrees()
    {
        // Arrange
        var classifyJson = """
            {"result":true,"confidence":0.9,"rationale":"Contains a bug-fix directive"}
            """;
        var docker = new Mock<IContainerManager>(MockBehavior.Strict);
        docker.Setup(d => d.ExecAsync(
                   It.IsAny<string>(),
                   It.IsAny<ContainerExecRequest>(),
                   It.IsAny<CancellationToken>()))
              .ReturnsAsync(new ExecResult(0, classifyJson, ""));

        var sut = BuildSut(docker: docker);
        var input = new ClassifierInput(
            Prompt: "Fix the null reference on page load",
            ClassificationQuestion: "Is this a bug fix?",
            ContainerId: "ctr-7",
            ModelPower: 3,
            AiAssistant: "claude");

        // Act
        var env = new ActivityEnvironment();
        var output = await env.RunAsync(() => sut.ClassifyAsync(input));

        // Assert
        output.Result.Should().BeTrue();
        output.Confidence.Should().Be(0.9m);
        output.Rationale.Should().Contain("bug-fix");
    }

    [Fact]
    public async Task ClassifyAsync_FallsBack_WhenJsonInvalid()
    {
        var docker = new Mock<IContainerManager>(MockBehavior.Strict);
        docker.Setup(d => d.ExecAsync(
                   It.IsAny<string>(),
                   It.IsAny<ContainerExecRequest>(),
                   It.IsAny<CancellationToken>()))
              .ReturnsAsync(new ExecResult(0, "maybe yes", ""));

        var sut = BuildSut(docker: docker);
        var input = new ClassifierInput(
            Prompt: "...",
            ClassificationQuestion: "Is this about databases?",
            ContainerId: "ctr-7",
            ModelPower: 3,
            AiAssistant: "claude");

        var env = new ActivityEnvironment();
        var output = await env.RunAsync(() => sut.ClassifyAsync(input));

        output.Result.Should().BeFalse();
        output.Confidence.Should().Be(0m);
        output.Rationale.Should().Be("parse-failure");
    }

    // ── RouteModelAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task RouteModelAsync_SelectsOpusForHighComplexity()
    {
        var sut = BuildSut(config: new MagicPaiConfig { DefaultAgent = "claude" });
        var input = new RouteModelInput(
            TaskCategory: "refactor",
            Complexity: 9,
            PreferredAgent: null);

        var env = new ActivityEnvironment();
        var output = await env.RunAsync(() => sut.RouteModelAsync(input));

        output.SelectedAgent.Should().Be("claude");
        output.SelectedModel.Should().Be("opus");        // complexity >= 8 → power 1
    }

    [Fact]
    public async Task RouteModelAsync_SelectsSonnetForMidComplexity()
    {
        var sut = BuildSut(config: new MagicPaiConfig { DefaultAgent = "claude" });
        var input = new RouteModelInput("code_gen", Complexity: 5, PreferredAgent: null);

        var env = new ActivityEnvironment();
        var output = await env.RunAsync(() => sut.RouteModelAsync(input));

        output.SelectedModel.Should().Be("sonnet");      // 4..7 → power 2
    }

    [Fact]
    public async Task RouteModelAsync_SelectsFastestForLowComplexity()
    {
        var sut = BuildSut(config: new MagicPaiConfig { DefaultAgent = "claude" });
        var input = new RouteModelInput("docs", Complexity: 2, PreferredAgent: null);

        var env = new ActivityEnvironment();
        var output = await env.RunAsync(() => sut.RouteModelAsync(input));

        output.SelectedModel.Should().Be("haiku");       // <4 → power 3
    }

    [Fact]
    public async Task RouteModelAsync_UsesPreferredAgent_WhenSpecified()
    {
        var sut = BuildSut(config: new MagicPaiConfig { DefaultAgent = "claude" });
        var input = new RouteModelInput(
            TaskCategory: "code_gen",
            Complexity: 9,
            PreferredAgent: "codex");

        var env = new ActivityEnvironment();
        var output = await env.RunAsync(() => sut.RouteModelAsync(input));

        output.SelectedAgent.Should().Be("codex");
        output.SelectedModel.Should().Be("gpt-5.4");     // codex power=1
    }

    // ── EnhancePromptAsync ───────────────────────────────────────────────

    [Fact]
    public async Task EnhancePromptAsync_ReturnsEnhanced_WhenAgentSucceeds()
    {
        var body = """
            {"enhancedPrompt":"Better version of the prompt","wasEnhanced":true,"rationale":"Added context"}
            """;
        var docker = new Mock<IContainerManager>(MockBehavior.Strict);
        docker.Setup(d => d.ExecAsync(
                   It.IsAny<string>(),
                   It.IsAny<ContainerExecRequest>(),
                   It.IsAny<CancellationToken>()))
              .ReturnsAsync(new ExecResult(0, body, ""));

        var sut = BuildSut(docker: docker);
        var input = new EnhancePromptInput(
            OriginalPrompt: "do the thing",
            EnhancementInstructions: "Make it crisp",
            ContainerId: "ctr-55",
            ModelPower: 2,
            AiAssistant: "claude");

        var env = new ActivityEnvironment();
        var output = await env.RunAsync(() => sut.EnhancePromptAsync(input));

        output.EnhancedPrompt.Should().Be("Better version of the prompt");
        output.WasEnhanced.Should().BeTrue();
        output.Rationale.Should().Be("Added context");
    }

    [Fact]
    public async Task EnhancePromptAsync_ReturnsOriginal_WhenParseFails()
    {
        var docker = new Mock<IContainerManager>(MockBehavior.Strict);
        docker.Setup(d => d.ExecAsync(
                   It.IsAny<string>(),
                   It.IsAny<ContainerExecRequest>(),
                   It.IsAny<CancellationToken>()))
              .ReturnsAsync(new ExecResult(0, "not json at all", ""));

        var sut = BuildSut(docker: docker);
        var input = new EnhancePromptInput(
            OriginalPrompt: "refactor login.js",
            EnhancementInstructions: "Polish the wording",
            ContainerId: "ctr-55",
            ModelPower: 2,
            AiAssistant: "claude");

        var env = new ActivityEnvironment();
        var output = await env.RunAsync(() => sut.EnhancePromptAsync(input));

        output.EnhancedPrompt.Should().Be("refactor login.js");  // falls back to original
        output.WasEnhanced.Should().BeFalse();
        output.Rationale.Should().Be("parse-failure");
    }

    // ── Day 5: ArchitectAsync ─────────────────────────────────────────────

    [Fact]
    public async Task ArchitectAsync_ParsesTaskList_FromNestedJson()
    {
        // Arrange — the runner's ParseResponse echoes input, so docker's output
        // is what ParseTasks sees. We exercise the { "tasks": [...] } shape.
        var planJson = """
            {
              "tasks": [
                { "id": "task-1", "description": "Create the login form", "dependsOn": [], "filesTouched": ["src/Login.razor"] },
                { "id": "task-2", "description": "Wire the login submit", "dependsOn": ["task-1"], "filesTouched": ["src/Login.razor.cs"] }
              ]
            }
            """;
        var docker = new Mock<IContainerManager>(MockBehavior.Strict);
        docker.Setup(d => d.ExecAsync(
                   It.IsAny<string>(),
                   It.IsAny<ContainerExecRequest>(),
                   It.IsAny<CancellationToken>()))
              .ReturnsAsync(new ExecResult(0, planJson, ""));

        var sut = BuildSut(docker: docker);
        var input = new ArchitectInput(
            Prompt: "Build a login page",
            ContainerId: "ctr-100",
            GapContext: null,
            AiAssistant: "claude");

        // Act
        var env = new ActivityEnvironment();
        var output = await env.RunAsync(() => sut.ArchitectAsync(input));

        // Assert
        output.TaskCount.Should().Be(2);
        output.Tasks.Should().HaveCount(2);
        output.Tasks[0].Id.Should().Be("task-1");
        output.Tasks[0].Description.Should().Contain("login form");
        output.Tasks[0].FilesTouched.Should().Contain("src/Login.razor");
        output.Tasks[1].DependsOn.Should().Contain("task-1");
        output.TaskListJson.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ArchitectAsync_ReturnsEmptyPlan_WhenParseFails()
    {
        var docker = new Mock<IContainerManager>(MockBehavior.Strict);
        docker.Setup(d => d.ExecAsync(
                   It.IsAny<string>(),
                   It.IsAny<ContainerExecRequest>(),
                   It.IsAny<CancellationToken>()))
              .ReturnsAsync(new ExecResult(0, "no json here at all", ""));

        var sut = BuildSut(docker: docker);
        var input = new ArchitectInput(
            Prompt: "whatever",
            ContainerId: "ctr-100",
            GapContext: "some gap info",
            AiAssistant: "claude");

        var env = new ActivityEnvironment();
        var output = await env.RunAsync(() => sut.ArchitectAsync(input));

        output.TaskCount.Should().Be(0);
        output.Tasks.Should().BeEmpty();
    }

    [Fact]
    public async Task ArchitectAsync_Throws_WhenContainerIdMissing()
    {
        var sut = BuildSut();
        var input = new ArchitectInput(
            Prompt: "any",
            ContainerId: "",
            GapContext: null,
            AiAssistant: "claude");

        var env = new ActivityEnvironment();
        Func<Task> act = async () => await env.RunAsync(() => sut.ArchitectAsync(input));

        await act.Should()
                 .ThrowAsync<Temporalio.Exceptions.ApplicationFailureException>()
                 .Where(e => e.ErrorType == "ConfigError");
    }

    // ── Day 5: ResearchPromptAsync ────────────────────────────────────────

    [Fact]
    public async Task ResearchPromptAsync_SplitsOutputIntoSections()
    {
        // Arrange — simulate streaming output via the callback. The activity
        // assembles chunks, then runs ParseResponse, then splits by H2 sections.
        var research = """
            Some preamble.
            ## Rewritten Task
            Build a login page with proper validation and aria-labels.
            ## Codebase Analysis
            src/Login.razor exists; uses MudBlazor.
            ## Research Context
            No external docs needed.
            ## Rationale
            Grounded in existing patterns.
            """;

        var docker = new Mock<IContainerManager>(MockBehavior.Strict);
        docker.Setup(d => d.ExecStreamingAsync(
                   It.IsAny<string>(),
                   It.IsAny<ContainerExecRequest>(),
                   It.IsAny<Action<string>>(),
                   It.IsAny<TimeSpan>(),
                   It.IsAny<CancellationToken>()))
              .Returns<string, ContainerExecRequest, Action<string>, TimeSpan, CancellationToken>(
                  (_, _, onOutput, _, _) =>
                  {
                      // Feed the chunk through the callback so the activity's
                      // StringBuilder assembly + heartbeating path runs.
                      onOutput(research);
                      return Task.FromResult(new ExecResult(0, "", ""));
                  });

        var sut = BuildSut(docker: docker);
        var input = new ResearchPromptInput(
            Prompt: "Build a login page",
            AiAssistant: "claude",
            ContainerId: "ctr-77",
            ModelPower: 2);

        // Act
        var env = new ActivityEnvironment();
        var output = await env.RunAsync(() => sut.ResearchPromptAsync(input));

        // Assert
        output.EnhancedPrompt.Should().Contain("Build a login page");
        output.CodebaseAnalysis.Should().Contain("MudBlazor");
        output.ResearchContext.Should().Contain("external docs");
        output.Rationale.Should().Contain("existing patterns");
    }

    [Fact]
    public async Task ResearchPromptAsync_Throws_WhenContainerIdMissing()
    {
        var sut = BuildSut();
        var input = new ResearchPromptInput(
            Prompt: "any",
            AiAssistant: "claude",
            ContainerId: "",
            ModelPower: 2);

        var env = new ActivityEnvironment();
        Func<Task> act = async () => await env.RunAsync(() => sut.ResearchPromptAsync(input));

        await act.Should()
                 .ThrowAsync<Temporalio.Exceptions.ApplicationFailureException>()
                 .Where(e => e.ErrorType == "ConfigError");
    }

    // ── Day 5: ClassifyWebsiteTaskAsync ───────────────────────────────────

    [Fact]
    public async Task ClassifyWebsiteTaskAsync_ReturnsTrue_WhenAgentAgrees()
    {
        // Delegates to ClassifyAsync under the hood with a fixed question.
        var classifyJson = """
            {"result":true,"confidence":0.95,"rationale":"Prompt mentions frontend styling"}
            """;
        var docker = new Mock<IContainerManager>(MockBehavior.Strict);
        docker.Setup(d => d.ExecAsync(
                   It.IsAny<string>(),
                   It.IsAny<ContainerExecRequest>(),
                   It.IsAny<CancellationToken>()))
              .ReturnsAsync(new ExecResult(0, classifyJson, ""));

        var sut = BuildSut(docker: docker);
        var input = new WebsiteClassifyInput(
            Prompt: "Redesign the landing page with better CTAs",
            ContainerId: "ctr-9",
            AiAssistant: "claude");

        var env = new ActivityEnvironment();
        var output = await env.RunAsync(() => sut.ClassifyWebsiteTaskAsync(input));

        output.IsWebsiteTask.Should().BeTrue();
        output.Confidence.Should().Be(0.95m);
        output.Rationale.Should().Contain("frontend");
    }

    [Fact]
    public async Task ClassifyWebsiteTaskAsync_ReturnsFalse_OnParseFailure()
    {
        var docker = new Mock<IContainerManager>(MockBehavior.Strict);
        docker.Setup(d => d.ExecAsync(
                   It.IsAny<string>(),
                   It.IsAny<ContainerExecRequest>(),
                   It.IsAny<CancellationToken>()))
              .ReturnsAsync(new ExecResult(0, "not structured", ""));

        var sut = BuildSut(docker: docker);
        var input = new WebsiteClassifyInput(
            Prompt: "Change the database index",
            ContainerId: "ctr-9",
            AiAssistant: "claude");

        var env = new ActivityEnvironment();
        var output = await env.RunAsync(() => sut.ClassifyWebsiteTaskAsync(input));

        output.IsWebsiteTask.Should().BeFalse();
        output.Rationale.Should().Be("parse-failure");
    }

    // ── Day 5: GradeCoverageAsync ─────────────────────────────────────────

    [Fact]
    public async Task GradeCoverageAsync_AllMet_ReturnsTrue()
    {
        var body = """
            {"allMet":true,"gapPrompt":"","report":"All requirements satisfied."}
            """;
        var docker = new Mock<IContainerManager>(MockBehavior.Strict);
        docker.Setup(d => d.ExecAsync(
                   It.IsAny<string>(),
                   It.IsAny<ContainerExecRequest>(),
                   It.IsAny<CancellationToken>()))
              .ReturnsAsync(new ExecResult(0, body, ""));

        var sut = BuildSut(docker: docker);
        var input = new CoverageInput(
            OriginalPrompt: "Add a /health endpoint",
            ContainerId: "ctr-44",
            WorkingDirectory: "/workspace",
            MaxIterations: 3,
            CurrentIteration: 1,
            ModelPower: 2,
            AiAssistant: "claude");

        var env = new ActivityEnvironment();
        var output = await env.RunAsync(() => sut.GradeCoverageAsync(input));

        output.AllMet.Should().BeTrue();
        output.GapPrompt.Should().BeEmpty();
        output.Iteration.Should().Be(1);
        output.CoverageReportJson.Should().Contain("All requirements");
    }

    [Fact]
    public async Task GradeCoverageAsync_FallsBack_WhenParseFails()
    {
        var docker = new Mock<IContainerManager>(MockBehavior.Strict);
        docker.Setup(d => d.ExecAsync(
                   It.IsAny<string>(),
                   It.IsAny<ContainerExecRequest>(),
                   It.IsAny<CancellationToken>()))
              .ReturnsAsync(new ExecResult(0, "not-json", ""));

        var sut = BuildSut(docker: docker);
        var input = new CoverageInput(
            OriginalPrompt: "ship feature",
            ContainerId: "ctr-44",
            WorkingDirectory: "/workspace",
            MaxIterations: 3,
            CurrentIteration: 2,
            ModelPower: 2,
            AiAssistant: "claude");

        var env = new ActivityEnvironment();
        var output = await env.RunAsync(() => sut.GradeCoverageAsync(input));

        output.AllMet.Should().BeFalse();
        output.GapPrompt.Should().Be("Retry in plain English.");
        output.Iteration.Should().Be(2);
    }

    // -- TruncateForHistory - Temporal-history payload cap (Optimization #134) --

    [Fact]
    public void TruncateForHistory_SmallInput_ReturnsUnchanged()
    {
        var input = "hello world";
        var result = AiActivities.TruncateForHistory(input, 8 * 1024);
        result.Should().Be(input);
    }

    [Fact]
    public void TruncateForHistory_NullInput_ReturnsEmpty()
    {
        var result = AiActivities.TruncateForHistory(null, 8 * 1024);
        result.Should().Be("");
    }

    [Fact]
    public void TruncateForHistory_LargeInput_KeepsTail()
    {
        var big = new string('A', 100_000) + "FINAL_ASSISTANT_MESSAGE";
        var result = AiActivities.TruncateForHistory(big, 8 * 1024);
        result.Length.Should().BeLessThan(big.Length);
        result.Should().Contain("FINAL_ASSISTANT_MESSAGE");
        result.Should().StartWith("[truncated");
    }

    [Fact]
    public void TruncateForHistory_MultiByteUnicode_CountedByBytes()
    {
        // Each "é" is 2 UTF-8 bytes; 5_000 chars = 10_000 bytes (larger than 8KB cap).
        var unicode = new string('é', 5_000) + "TAIL";
        var result = AiActivities.TruncateForHistory(unicode, 8 * 1024);
        System.Text.Encoding.UTF8.GetByteCount(result).Should().BeLessThan(unicode.Length * 2);
        result.Should().Contain("TAIL");
    }

    [Fact]
    public void TruncateForHistory_EmptyString_ReturnsEmpty()
    {
        AiActivities.TruncateForHistory("", 1024).Should().Be("");
    }

    [Fact]
    public void TruncateForHistory_MaxBytesOne_StillTruncatesToMinimumTail()
    {
        // maxBytes=1 forces the truncation path; the floor of 1024 bytes for the
        // trailing window still applies because losing the entire tail defeats
        // the purpose — it's better to overshoot maxBytes slightly than return
        // a useless header-only string.
        var big = new string('A', 10_000);
        var result = AiActivities.TruncateForHistory(big, 1);
        result.Should().StartWith("[truncated");
        result.Should().Contain("A");
    }

    // -- NormalizeContainerWorkDir - Fix #118 host-path coercion --

    private static AiActivities MakeSutForCwd(string? configCwd = "/workspace")
    {
        var factory = new Mock<ICliAgentFactory>(MockBehavior.Loose);
        var docker = new Mock<IContainerManager>(MockBehavior.Loose);
        var sink = new Mock<ISessionStreamSink>(MockBehavior.Loose);
        var config = new MagicPaiConfig { ContainerWorkDir = configCwd! };
        var auth = new AuthRecoveryService(config);
        return new AiActivities(factory.Object, docker.Object, sink.Object, auth, config, NullLogger<AiActivities>.Instance);
    }

    [Fact]
    public void NormalizeContainerWorkDir_LinuxPath_PassesThrough()
    {
        var sut = MakeSutForCwd();
        sut.NormalizeContainerWorkDir("/workspace/sub").Should().Be("/workspace/sub");
        sut.NormalizeContainerWorkDir("/tmp").Should().Be("/tmp");
    }

    [Fact]
    public void NormalizeContainerWorkDir_WindowsPath_CoercesToDefault()
    {
        var sut = MakeSutForCwd();
        sut.NormalizeContainerWorkDir("C:/tmp/foo").Should().Be("/workspace");
        sut.NormalizeContainerWorkDir("C:\tmp\foo").Should().Be("/workspace");
        sut.NormalizeContainerWorkDir("D:/path").Should().Be("/workspace");
    }

    [Fact]
    public void NormalizeContainerWorkDir_NullOrEmpty_UsesDefault()
    {
        var sut = MakeSutForCwd();
        sut.NormalizeContainerWorkDir(null).Should().Be("/workspace");
        sut.NormalizeContainerWorkDir("").Should().Be("/workspace");
        sut.NormalizeContainerWorkDir("   ").Should().Be("/workspace");
    }

    [Fact]
    public void NormalizeContainerWorkDir_RelativePath_CoercesToDefault()
    {
        var sut = MakeSutForCwd();
        sut.NormalizeContainerWorkDir("sub").Should().Be("/workspace");
        sut.NormalizeContainerWorkDir("./sub").Should().Be("/workspace");
    }

    [Fact]
    public void NormalizeContainerWorkDir_NullConfig_UsesWorkspaceLiteral()
    {
        var sut = MakeSutForCwd(configCwd: null);
        sut.NormalizeContainerWorkDir("C:/tmp").Should().Be("/workspace");
    }
}
