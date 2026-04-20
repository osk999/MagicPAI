using System.Diagnostics;
using FluentAssertions;
using MagicPAI.Activities.AI;
using MagicPAI.Activities.Contracts;
using MagicPAI.Core.Config;
using MagicPAI.Core.Models;
using MagicPAI.Core.Services;
using MagicPAI.Core.Services.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using Temporalio.Testing;
using Xunit.Abstractions;

namespace MagicPAI.Tests.Activities.Live;

/// <summary>
/// Live E2E verification for the three triage/classifier AI activities.
/// Spins up a real <c>magicpai-env</c> container, calls
/// <see cref="AiActivities.TriageAsync"/>, <see cref="AiActivities.ClassifyAsync"/>,
/// and <see cref="AiActivities.ClassifyWebsiteTaskAsync"/> with a real
/// <see cref="ClaudeRunner"/>, and asserts the returned structured output is
/// produced by Claude (i.e. <b>not</b> the fallback defaults).
///
/// <para>
/// Tags: <c>[Trait("Category", "ClassifierE2E")]</c>. Tests share one spawned
/// container via <c>IClassFixture</c>-style <see cref="IAsyncLifetime"/> — a
/// single <c>magicpai-env</c> pod is spawned in <see cref="InitializeAsync"/>
/// and destroyed in <see cref="DisposeAsync"/>. Host Claude credentials must
/// be present in <c>$USERPROFILE/.claude.json</c> or
/// <c>$USERPROFILE/.claude/.credentials.json</c> (mounted into the container
/// by <see cref="DockerContainerManager.BuildCredentialBinds"/>). If they are
/// absent, the live tests are skipped.
/// </para>
/// </summary>
[Trait("Category", "ClassifierE2E")]
public class ClassifierLiveDockerTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly DockerContainerManager _docker;
    private readonly CliAgentFactory _factory;
    private readonly AuthRecoveryService _auth;
    private readonly MagicPaiConfig _config;
    private readonly AiActivities _sut;
    private string? _containerId;
    private string? _workspaceDir;

    public ClassifierLiveDockerTests(ITestOutputHelper output)
    {
        _output = output;
        _docker = new DockerContainerManager();
        _factory = new CliAgentFactory();
        _config = new MagicPaiConfig
        {
            UseDocker = true,
            WorkerImage = "magicpai-env:latest",
            DefaultAgent = "claude",
            DefaultModel = "auto",
            ContainerWorkDir = "/workspace",
            AgentTimeoutMinutes = 10,
        };
        _auth = new AuthRecoveryService(_config);
        _sut = new AiActivities(
            factory: _factory,
            docker: _docker,
            sink: new NullSessionStreamSink(),
            auth: _auth,
            config: _config,
            log: NullLogger<AiActivities>.Instance);
    }

    public async Task InitializeAsync()
    {
        if (!HostHasClaudeCredentials())
        {
            _output.WriteLine("Host Claude credentials missing — live tests will skip.");
            return;
        }

        _workspaceDir = Path.Combine(Path.GetTempPath(),
            "magicpai-classifier-e2e-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_workspaceDir);

        var spawned = await _docker.SpawnAsync(new ContainerConfig
        {
            Image = "magicpai-env:latest",
            WorkspacePath = _workspaceDir,
            ContainerWorkDir = "/workspace",
            MemoryLimitMb = 2048,
            CpuCount = 2,
            EnableGui = false,
        }, CancellationToken.None);

        _containerId = spawned.ContainerId;
        _output.WriteLine($"Spawned container {_containerId} on {_workspaceDir}");

        var ready = await WaitForClaudeReadyAsync(_containerId, TimeSpan.FromSeconds(90));
        if (!ready)
            _output.WriteLine("WARNING: claude CLI not yet ready after 90s — tests may still pass if entrypoint completes during call.");
    }

    public async Task DisposeAsync()
    {
        if (_containerId is not null)
        {
            try
            {
                await _docker.DestroyAsync(_containerId, CancellationToken.None);
                _output.WriteLine($"Destroyed container {_containerId}");
                var leak = await GetContainerStatusAsync(_containerId);
                _output.WriteLine($"Post-destroy status: {leak}");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Failed to destroy container {_containerId}: {ex.Message}");
            }
        }
        if (_workspaceDir is not null && Directory.Exists(_workspaceDir))
        {
            try { Directory.Delete(_workspaceDir, recursive: true); } catch { /* best-effort */ }
        }
        _docker.Dispose();
    }

    // ────────────────────────────────────────────────────────────────────────
    // Tests
    // ────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task TriageAsync_Live_ProducesStructuredTriageFromClaude()
    {
        Skip.IfNot(HostHasClaudeCredentials(), "Host Claude credentials not present; skipping live E2E.");
        Skip.If(_containerId is null, "Container did not spawn; skipping.");

        var input = new TriageInput(
            Prompt: "Refactor the authentication subsystem across four modules, splitting the " +
                    "token exchange from the session store and adding a pluggable recovery strategy",
            ContainerId: _containerId!,
            ClassificationInstructions: null,
            AiAssistant: "claude",
            ComplexityThreshold: 7);

        var env = new ActivityEnvironment();
        var running = await GetContainerStatusAsync(_containerId!);
        _output.WriteLine($"Pre-triage container status: {running}");

        var sw = Stopwatch.StartNew();
        var output = await env.RunAsync(() => _sut.TriageAsync(input));
        sw.Stop();

        _output.WriteLine(
            $"Triage ({sw.Elapsed.TotalSeconds:F1}s) => complexity={output.Complexity} " +
            $"category={output.Category} power={output.RecommendedModelPower} " +
            $"model={output.RecommendedModel} needsDecomp={output.NeedsDecomposition} " +
            $"isComplex={output.IsComplex}");

        // Structured output assertions — must NOT be the static fallback.
        output.Complexity.Should().BeInRange(1, 10, "Claude should return a valid complexity score");
        output.Category.Should().BeOneOf(
            "code_gen", "bug_fix", "refactor", "architecture", "testing", "docs");
        output.RecommendedModel.Should().NotBeNullOrWhiteSpace();

        var looksLikeFallback =
            output.Complexity == 5
            && output.Category == "code_gen"
            && output.RecommendedModelPower == 2
            && output.NeedsDecomposition == false;
        looksLikeFallback.Should().BeFalse(
            "exact fallback tuple indicates Claude did not produce JSON — classifier degraded to static default");

        // A cross-module refactor should not rate below 4/10.
        output.Complexity.Should().BeGreaterOrEqualTo(
            4, "a cross-module refactor should not rate below 4/10");

        // IsComplex is derived — verify it tracks Complexity vs threshold.
        output.IsComplex.Should().Be(output.Complexity >= input.ComplexityThreshold);
    }

    [SkippableFact]
    public async Task ClassifyAsync_Live_ProducesBooleanAnswerFromClaude()
    {
        Skip.IfNot(HostHasClaudeCredentials(), "Host Claude credentials not present; skipping live E2E.");
        Skip.If(_containerId is null, "Container did not spawn; skipping.");

        var input = new ClassifierInput(
            Prompt: "The login button throws a NullReferenceException when clicked on the dashboard page",
            ClassificationQuestion: "Is this a bug fix task?",
            ContainerId: _containerId!,
            ModelPower: 3,
            AiAssistant: "claude");

        var env = new ActivityEnvironment();
        var sw = Stopwatch.StartNew();
        var output = await env.RunAsync(() => _sut.ClassifyAsync(input));
        sw.Stop();

        _output.WriteLine(
            $"Classify ({sw.Elapsed.TotalSeconds:F1}s) => result={output.Result} " +
            $"confidence={output.Confidence} rationale={Truncate(output.Rationale, 200)}");

        // parse-failure rationale is the catch-all: it means either Claude
        // returned nothing parseable OR the structured JSON was empty.
        output.Rationale.Should().NotBe("parse-failure",
            "'parse-failure' is the fallback rationale — classifier did not get JSON from Claude");
        output.Rationale.Should().NotBeNullOrWhiteSpace();
        output.Result.Should().BeTrue(
            "the prompt clearly describes a crash-on-click bug; a sensible classifier should say yes");
        output.Confidence.Should().BeGreaterThan(0m,
            "parsed confidence should exceed the 0m fallback default");
    }

    [SkippableFact]
    public async Task ClassifyWebsiteTaskAsync_Live_DelegatesToClassifyAndReturnsWebsiteFlag()
    {
        Skip.IfNot(HostHasClaudeCredentials(), "Host Claude credentials not present; skipping live E2E.");
        Skip.If(_containerId is null, "Container did not spawn; skipping.");

        var input = new WebsiteClassifyInput(
            Prompt: "Build a responsive landing page with a hero section, pricing cards, and a " +
                    "sticky navigation bar in React and Tailwind",
            ContainerId: _containerId!,
            AiAssistant: "claude");

        var env = new ActivityEnvironment();
        var sw = Stopwatch.StartNew();
        var output = await env.RunAsync(() => _sut.ClassifyWebsiteTaskAsync(input));
        sw.Stop();

        _output.WriteLine(
            $"ClassifyWebsiteTask ({sw.Elapsed.TotalSeconds:F1}s) => isWebsite={output.IsWebsiteTask} " +
            $"confidence={output.Confidence} rationale={Truncate(output.Rationale, 200)}");

        output.Rationale.Should().NotBe("parse-failure",
            "delegated Classify returned the parse-failure fallback — structured output missing");
        output.Rationale.Should().NotBeNullOrWhiteSpace();
        output.IsWebsiteTask.Should().BeTrue(
            "the prompt explicitly builds a web page with React/Tailwind — a website classification");
        output.Confidence.Should().BeGreaterThan(0m);
    }

    [Fact]
    public async Task AllClassifiers_ContainerIdMissing_ThrowsConfigErrorWithoutInvokingDocker()
    {
        // This case does NOT require Claude credentials — it verifies the
        // up-front ContainerId guard throws a non-retryable
        // ApplicationFailureException of type "ConfigError" for all three
        // classifier entrypoints. No Docker call should happen.
        var env = new ActivityEnvironment();

        var triageInput = new TriageInput(
            Prompt: "anything", ContainerId: "", ClassificationInstructions: null,
            AiAssistant: "claude");
        var classifyInput = new ClassifierInput(
            Prompt: "x", ClassificationQuestion: "y?", ContainerId: "", ModelPower: 3,
            AiAssistant: "claude");
        var websiteInput = new WebsiteClassifyInput(
            Prompt: "x", ContainerId: "", AiAssistant: "claude");

        var triageEx = await Record.ExceptionAsync(
            () => env.RunAsync(() => _sut.TriageAsync(triageInput)));
        var classifyEx = await Record.ExceptionAsync(
            () => env.RunAsync(() => _sut.ClassifyAsync(classifyInput)));
        var websiteEx = await Record.ExceptionAsync(
            () => env.RunAsync(() => _sut.ClassifyWebsiteTaskAsync(websiteInput)));

        triageEx.Should().BeOfType<Temporalio.Exceptions.ApplicationFailureException>()
            .Which.ErrorType.Should().Be("ConfigError");
        classifyEx.Should().BeOfType<Temporalio.Exceptions.ApplicationFailureException>()
            .Which.ErrorType.Should().Be("ConfigError");
        // ClassifyWebsiteTaskAsync delegates → the inner Classify guard fires.
        websiteEx.Should().BeOfType<Temporalio.Exceptions.ApplicationFailureException>()
            .Which.ErrorType.Should().Be("ConfigError");
    }

    // ────────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────────

    private static bool HostHasClaudeCredentials()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var claudeJson = Path.Combine(userProfile, ".claude.json");
        var credsJson = Path.Combine(userProfile, ".claude", ".credentials.json");
        return File.Exists(claudeJson) || File.Exists(credsJson);
    }

    private async Task<bool> WaitForClaudeReadyAsync(string containerId, TimeSpan budget)
    {
        using var cts = new CancellationTokenSource(budget);
        var deadline = DateTime.UtcNow + budget;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var res = await _docker.ExecAsync(
                    containerId,
                    "test -f /home/worker/.claude/.credentials.json -o -f /home/worker/.claude.json && which claude",
                    "/workspace",
                    cts.Token);
                if (res.ExitCode == 0)
                    return true;
            }
            catch
            {
                // container may still be starting; retry
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cts.Token);
        }
        return false;
    }

    private static async Task<string> GetContainerStatusAsync(string containerId)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "docker",
                ArgumentList = { "inspect", containerId, "--format", "{{.State.Status}}" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi)!;
            var stdout = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            return p.ExitCode == 0 ? stdout.Trim() : "not-found";
        }
        catch (Exception ex)
        {
            return $"error:{ex.Message}";
        }
    }

    private static string Truncate(string? s, int n)
        => string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s[..n] + "...");

    private sealed class NullSessionStreamSink : ISessionStreamSink
    {
        public Task EmitChunkAsync(string sessionId, string line, CancellationToken ct) => Task.CompletedTask;
        public Task EmitStructuredAsync(string sessionId, string eventName, object payload, CancellationToken ct) => Task.CompletedTask;
        public Task EmitStageAsync(string sessionId, string stage, CancellationToken ct) => Task.CompletedTask;
        public Task CompleteSessionAsync(string sessionId, CancellationToken ct) => Task.CompletedTask;
    }
}
