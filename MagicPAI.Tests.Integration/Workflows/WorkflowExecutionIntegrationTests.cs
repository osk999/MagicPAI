using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MagicPAI.Core.Models;
using MagicPAI.Server.Bridge;
using MagicPAI.Server.Controllers;
using MagicPAI.Tests.Integration.Fixtures;
using MagicPAI.Tests.Integration.Stubs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MagicPAI.Tests.Integration.Workflows;

public class WorkflowExecutionIntegrationTests : IntegrationTestBase
{
    public WorkflowExecutionIntegrationTests(MagicPaiWebApplicationFactory factory) : base(factory)
    {
        Factory.ContainerManager.Reset();
    }

    [Fact]
    public async Task FullOrchestrate_SimplePath_Completes_WithClassifierInsight_And_Output()
    {
        ConfigureSimpleExecution();

        var sessionId = await CreateSessionWithRetryAsync("Simple integration task", "full-orchestrate");
        var session = await WaitForTerminalStateAsync(sessionId);
        var activities = await WaitForActivitiesAsync(sessionId, "website-classifier", "triage", "simple-agent", "simple-verify", "destroy-container");
        var logs = await WaitForExecutionLogsAsync(sessionId, "WebsiteTaskResult", "TriageResult");

        Assert.True(
            string.Equals(session.State, "completed", StringComparison.Ordinal),
            $"Expected completed but was '{session.State}'. Activities: {string.Join(", ", activities.Select(x => $"{x.Name}:{x.Status}"))}. Logs: {string.Join(" | ", logs.Select(x => $"{x.EventName}:{x.Message}"))}");
        Assert.Equal("claude", session.Agent);
        Assert.Contains(activities, x => x.Name == "website-classifier" && x.Status == "completed");
        Assert.Contains(activities, x => x.Name == "triage" && x.Status == "completed");
        Assert.Contains(activities, x => x.Name == "simple-agent");
        Assert.DoesNotContain(activities, x => x.Name == "architect");
        Assert.Contains(logs, x => x.EventName == "WebsiteTaskResult" && x.Message.Contains("\"verdict\":\"NonWebsite\"", StringComparison.Ordinal));
        Assert.Contains(logs, x => x.EventName == "TriageResult" && x.Message.Contains("\"outcome\":\"Simple\"", StringComparison.Ordinal));
        Assert.Contains(Factory.ContainerManager.ExecInvocations, x => x.IsStreaming && (TryGetRequestArg(x.Request, "--prompt") ?? "").Contains("Simple integration task", StringComparison.Ordinal));
        Assert.Contains(Factory.ContainerManager.ExecInvocations, x => IsBuildCommand(x.Command));
        Assert.Contains(Factory.ContainerManager.ExecInvocations, x => IsTestCommand(x.Command));
        Assert.Single(Factory.ContainerManager.DestroyedContainers);
    }

    [Fact]
    public async Task FullOrchestrate_ComplexPath_Repairs_Then_Completes()
    {
        ConfigureComplexExecutionWithRepair();

        var sessionId = await CreateSessionWithRetryAsync("Complex integration task", "full-orchestrate");
        var session = await WaitForTerminalStateAsync(sessionId);
        var activities = await WaitForActivitiesAsync(sessionId, "website-classifier", "triage", "architect", "complex-agent", "complex-verify", "complex-repair", "repair-agent", "destroy-container");
        var logs = await WaitForExecutionLogsAsync(sessionId, "WebsiteTaskResult", "TriageResult", "ArchitectResult", "RepairPromptGenerated");
        await WaitForDestroyedContainersAsync(1);

        Assert.True(
            string.Equals(session.State, "completed", StringComparison.Ordinal),
            $"Expected completed but was '{session.State}'. Activities: {string.Join(", ", activities.Select(x => $"{x.Name}:{x.Status}"))}. Logs: {string.Join(" | ", logs.Select(x => $"{x.EventName}:{x.Message}"))}");
        Assert.Contains(activities, x => x.Name == "website-classifier" && x.Status == "completed");
        Assert.Contains(activities, x => x.Name == "triage" && x.Status == "completed");
        Assert.Contains(activities, x => x.Name == "architect" && x.Status == "completed");
        Assert.Contains(activities, x => x.Name == "complex-repair");
        Assert.Contains(activities, x => x.Name == "repair-agent");
        Assert.Contains(logs, x => x.EventName == "WebsiteTaskResult" && x.Message.Contains("\"verdict\":\"NonWebsite\"", StringComparison.Ordinal));
        Assert.Contains(logs, x => x.EventName == "TriageResult" && x.Message.Contains("\"outcome\":\"Complex\"", StringComparison.Ordinal));
        Assert.Contains(logs, x => x.EventName == "ArchitectResult" && x.Message.Contains("\"taskCount\":2", StringComparison.Ordinal));
        Assert.Contains(logs, x => x.EventName == "RepairPromptGenerated");
        Assert.Contains(Factory.ContainerManager.ExecInvocations, x => x.IsStreaming && (TryGetRequestArg(x.Request, "--prompt") ?? "").Contains("The previous attempt had verification failures", StringComparison.Ordinal));

        var buildCommands = Factory.ContainerManager.ExecInvocations
            .Where(x => x.Command?.Contains("dotnet build", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();
        Assert.True(buildCommands.Count >= 2, "Expected build verification to run before and after repair.");
        Assert.Single(Factory.ContainerManager.DestroyedContainers);
    }

    [Fact]
    public async Task StandardOrchestrate_Enhancement_Emits_BeforeAfter_Insight()
    {
        ConfigureStandardSimpleExecution();

        var sessionId = await CreateSessionWithRetryAsync("Improve this vague task", "standard-orchestrate");
        var session = await WaitForTerminalStateAsync(sessionId);
        var activities = await WaitForActivitiesAsync(sessionId, "std-enhance", "std-elaborate", "std-context", "std-triage");
        var logs = await WaitForExecutionLogsAsync(sessionId, "PromptTransform");

        Assert.Equal("completed", session.State);
        Assert.Contains(activities, x => x.Name == "std-triage");
        Assert.Contains(logs, x => x.EventName == "PromptTransform" &&
                                   x.Message.Contains("\"label\":\"Prompt Enhancement\"", StringComparison.Ordinal) &&
                                   x.Message.Contains("\"verdict\":\"changed\"", StringComparison.Ordinal) &&
                                   x.Message.Contains("\"before\":\"Improve this vague task\"", StringComparison.Ordinal) &&
                                   x.Message.Contains("\"after\":\"Expanded prompt: break the work into explicit acceptance criteria.\"", StringComparison.Ordinal));
        var prompts = Factory.ContainerManager.ExecInvocations
            .Where(x => x.IsStreaming && x.Request?.FileName == "stub-agent")
            .Select(x => TryGetRequestArg(x.Request, "--prompt") ?? "")
            .ToList();
        Assert.Contains(prompts, x => x.Contains("Improve this vague task", StringComparison.Ordinal));
        Assert.True(prompts.Count >= 3, "Expected enhancement, elaboration/context, and execution prompts.");
    }

    [Fact]
    public async Task FullOrchestrate_TriageFailure_FailsVisibly_And_CleansUp()
    {
        ConfigureTriageFailureExecution();

        var sessionId = await CreateSessionWithRetryAsync("Triage should fail loudly", "full-orchestrate");
        var session = await WaitForTerminalStateAsync(sessionId);
        var activities = await WaitForActivitiesAsync(sessionId, "spawn-container", "website-classifier");
        var logs = await WaitForExecutionLogsAsync(sessionId, "WebsiteTaskClassificationFailed");
        await WaitForDestroyedContainersAsync(1);

        Assert.Equal("failed", session.State);
        Assert.Contains(activities, x => x.Name == "website-classifier" && x.Status == "failed");
        Assert.Contains(logs, x => x.EventName == "WebsiteTaskClassificationFailed");
        Assert.Single(Factory.ContainerManager.DestroyedContainers);
    }

    [Fact]
    public async Task FullOrchestrate_PersistsGuiUrl_And_ContainerLogs()
    {
        ConfigureSimpleExecution();
        Factory.ContainerManager.ContainerLogProvider = _ => ["worker booting", "browser ready"];

        var sessionId = await CreateSessionWithRetryAsync("Simple integration task", "full-orchestrate");
        var session = await WaitForTerminalStateAsync(sessionId);
        var output = await Client.GetFromJsonAsync<string[]>($"/api/sessions/{sessionId}/output") ?? [];

        Assert.Equal("completed", session.State);
        Assert.NotNull(session.GuiUrl);
        Assert.Contains("http://localhost:6080/", session.GuiUrl, StringComparison.Ordinal);
        Assert.NotNull(session.LastContainerLogAt);
        Assert.Contains(output, x => x.Contains("[container] worker booting", StringComparison.Ordinal));
        Assert.Contains(output, x => x.Contains("[container] browser ready", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LoopVerifier_Completes_After_Bounded_Iterations()
    {
        ConfigureLoopVerifierExecution();

        var sessionId = await CreateSessionWithRetryAsync("Continue until the task is done", "loop-verifier");
        var session = await WaitForTerminalStateAsync(sessionId);
        var activities = await WaitForActivitiesAsync(sessionId,
            "loop-spawn",
            "loop-iteration-gate",
            "loop-runner",
            "loop-classifier");
        var logs = await WaitForExecutionLogsAsync(sessionId, "TriageResult");

        Assert.Equal("completed", session.State);
        Assert.Contains(activities, x => x.Name == "loop-classifier");
        Assert.True(
            logs.Count(x => x.EventName == "TriageResult") >= 2,
            $"Expected at least two loop classifier decisions. Activities: {string.Join(", ", activities.Select(x => $"{x.Name}:{x.Status}"))}. Logs: {string.Join(" | ", logs.Select(x => $"{x.EventName}:{x.Message}"))}. Streaming invocations: {Factory.ContainerManager.ExecInvocations.Count(x => x.IsStreaming)}");
        Assert.Contains(logs, x => x.EventName == "TriageResult" && x.Message.Contains("\"outcome\":\"Complex\"", StringComparison.Ordinal));
        Assert.True(
            logs.Any(x => x.EventName == "TriageResult" && x.Message.Contains("\"outcome\":\"Simple\"", StringComparison.Ordinal)),
            $"Missing simple verdict. Logs: {string.Join(" | ", logs.Select(x => $"{x.EventName}:{x.Message}"))}");
        Assert.Equal(2, Factory.ContainerManager.ExecInvocations.Count(x => x.IsStreaming));
        Assert.Single(Factory.ContainerManager.DestroyedContainers);
    }

    [Fact]
    public async Task WebsiteAuditLoop_Completes_All_Phases_And_CleansUp()
    {
        ConfigureWebsiteAuditExecution();

        var sessionId = await CreateSessionWithRetryAsync(
            "Audit the sample marketing website at http://localhost:5010 and report issues",
            "website-audit-loop");
        var session = await WaitForTerminalStateAsync(sessionId);
        var activities = await WaitForActivitiesAsync(sessionId,
            "audit-spawn",
            "phase1-discovery-gate",
            "phase1-discovery-runner",
            "phase1-discovery-check",
            "phase2-visual-runner",
            "phase2-visual-check",
            "phase3-interaction-runner",
            "phase3-interaction-check");
        var logs = await WaitForExecutionLogsAsync(sessionId, "TriageResult");

        Assert.Equal("completed", session.State);
        Assert.True(
            Factory.ContainerManager.ExecInvocations.Any(x =>
                x.IsStreaming &&
                (TryGetRequestArg(x.Request, "--prompt") ?? "").Contains("Phase 4: Final re-verification and synthesis", StringComparison.Ordinal)),
            $"Missing final synthesis run. Activities: {string.Join(", ", activities.Select(x => $"{x.Name}:{x.Status}"))}. Logs: {string.Join(" | ", logs.Select(x => $"{x.EventName}:{x.Message}"))}. Streaming prompts: {string.Join(" || ", Factory.ContainerManager.ExecInvocations.Where(x => x.IsStreaming).Select(x => TryGetRequestArg(x.Request, "--prompt") ?? ""))}");
        Assert.True(logs.Count(x => x.EventName == "TriageResult") >= 4, $"Expected discovery, visual, and interaction checks to emit classifier results. Logs: {string.Join(" | ", logs.Select(x => $"{x.EventName}:{x.Message}"))}");
        Assert.True(Factory.ContainerManager.ExecInvocations.Count(x => x.IsStreaming) >= 5, $"Expected discovery loop plus later audit phases. Streaming prompts: {string.Join(" || ", Factory.ContainerManager.ExecInvocations.Where(x => x.IsStreaming).Select(x => TryGetRequestArg(x.Request, "--prompt") ?? ""))}");
        Assert.Single(Factory.ContainerManager.DestroyedContainers);
    }

    [Fact]
    public async Task WebsiteAuditLoop_Isolates_Assistant_Sessions_Per_Activity()
    {
        ConfigureWebsiteAuditExecutionWithScopedSessions();

        var sessionId = await CreateSessionWithRetryAsync(
            "Audit the sample marketing website at http://localhost:5010 and report issues",
            "website-audit-loop");
        var session = await WaitForTerminalStateAsync(sessionId);

        Assert.Equal("completed", session.State);

        var streamingInvocations = Factory.ContainerManager.ExecInvocations
            .Where(x => x.IsStreaming && x.Request?.FileName == "stub-agent")
            .ToList();

        var discoveryInvocations = streamingInvocations
            .Where(x => (TryGetRequestArg(x.Request, "--prompt") ?? "").Contains("Phase 1: Discovery", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(2, discoveryInvocations.Count);
        Assert.Equal("", TryGetRequestArg(discoveryInvocations[0].Request, "--session") ?? "");
        Assert.Equal("discovery-session", TryGetRequestArg(discoveryInvocations[1].Request, "--session"));

        var visualInvocation = Assert.Single(streamingInvocations
            .Where(x => (TryGetRequestArg(x.Request, "--prompt") ?? "").Contains("Phase 2: Visual audit", StringComparison.Ordinal)));
        var interactionInvocation = Assert.Single(streamingInvocations
            .Where(x => (TryGetRequestArg(x.Request, "--prompt") ?? "").Contains("Phase 3: Interaction and scroll audit", StringComparison.Ordinal)));
        var finalInvocation = Assert.Single(streamingInvocations
            .Where(x => (TryGetRequestArg(x.Request, "--prompt") ?? "").Contains("Phase 4: Final re-verification and synthesis", StringComparison.Ordinal)));

        Assert.Equal("", TryGetRequestArg(visualInvocation.Request, "--session") ?? "");
        Assert.Equal("", TryGetRequestArg(interactionInvocation.Request, "--session") ?? "");
        Assert.Equal("", TryGetRequestArg(finalInvocation.Request, "--session") ?? "");
    }

    [Fact]
    public async Task FullOrchestrate_CssWebsiteTask_Routes_Through_WebsiteAudit()
    {
        ConfigureWebsiteFullOrchestrateExecution();

        const string prompt = """
            mission:
              workdir: C:\AllGit\CSharp\IsraelRiseDonationSystem
            prompt:
            yochange the entire css of ramathayal770
            its need to look diffrent then simple donate
            you need to find color, fonts, whatever needed based on the details and based on simple website
            """;

        var sessionId = await CreateSessionWithRetryAsync(prompt, "full-orchestrate");
        var session = await WaitForTerminalStateAsync(sessionId);
        var activities = await WaitForActivitiesAsync(sessionId,
            "website-classifier",
            "triage",
            "simple-path",
            "website-audit",
            "phase1-discovery-runner",
            "phase2-visual-runner",
            "phase3-interaction-runner",
            "destroy-container");
        var logs = await WaitForExecutionLogsAsync(sessionId, "WebsiteTaskResult", "TriageResult");

        Assert.Equal("completed", session.State);
        Assert.Contains(activities, x => x.Name == "website-classifier" && x.Status == "completed");
        Assert.Contains(activities, x => x.Name == "website-audit");
        Assert.Contains(activities, x => x.Name == "phase1-discovery-runner");
        Assert.Contains(activities, x => x.Name == "phase2-visual-runner");
        Assert.Contains(activities, x => x.Name == "phase3-interaction-runner");
        Assert.Contains(logs, x => x.EventName == "WebsiteTaskResult" && x.Message.Contains("\"verdict\":\"Website\"", StringComparison.Ordinal));
        Assert.True(
            Factory.ContainerManager.ExecInvocations.Any(x =>
                !x.IsStreaming &&
                x.Request?.FileName == "stub-agent" &&
                (TryGetRequestArg(x.Request, "--prompt") ?? "").Contains("IMPORTANT: CSS/UI/UX fix tasks ALWAYS route to WEBSITE AUDIT (true).", StringComparison.Ordinal)),
            "Expected the website classifier prompt to contain the CSS/UI/UX website-audit routing rule.");
        Assert.True(
            Factory.ContainerManager.ExecInvocations.Any(x =>
                x.IsStreaming &&
                (TryGetRequestArg(x.Request, "--prompt") ?? "").Contains("Phase 4: Final re-verification and synthesis", StringComparison.Ordinal)),
            "Expected the post-implementation website audit to reach the final re-verification phase.");
        Assert.Single(Factory.ContainerManager.DestroyedContainers);
    }

    private void ConfigureSimpleExecution()
    {
        Factory.ContainerManager.ExecHandler = invocation =>
        {
            var prompt = TryGetRequestArg(invocation.Request, "--prompt") ?? "";
            if (!invocation.IsStreaming && invocation.Request?.FileName == "stub-agent" &&
                prompt.Contains("Decide whether this task should route into a browser-based website audit workflow.", StringComparison.Ordinal))
                return WebsiteRoutingEnvelope(false, 9, "This is a code task, not a website audit.");

            if (!invocation.IsStreaming && invocation.Request?.FileName == "stub-agent")
                return AgentEnvelope("""{"complexity":3,"category":"bug_fix","recommended_model_power":3,"needs_decomposition":false}""");

            if (IsCompileProbe(invocation.Command))
                return new ExecResult(0, "MagicPAI.Server.csproj", "");
            if (IsTestProbe(invocation.Command))
                return new ExecResult(0, "MagicPAI.Tests.csproj", "");
            if (IsBuildCommand(invocation.Command))
                return new ExecResult(0, "Build succeeded", "");
            if (IsTestCommand(invocation.Command))
                return new ExecResult(0, "Passed", "");
            if (IsFileListCommand(invocation.Command))
                return new ExecResult(0, "./MagicPAI.Server/Program.cs\n./MagicPAI.Server/MagicPAI.Server.csproj", "");
            if (IsSourceScanCommand(invocation.Command))
                return new ExecResult(0, "", "");

            return Factory.ContainerManager.DefaultExecResult;
        };

        Factory.ContainerManager.StreamingHandler = invocation =>
        {
            var prompt = TryGetRequestArg(invocation.Request, "--prompt") ?? "";

            if (prompt.Contains("Simple integration task", StringComparison.Ordinal))
            {
                return new StubStreamingPlan(
                    AgentEnvelope("Simple worker completed."),
                    [new StubOutputChunk("[simple] starting\n"), new StubOutputChunk("[simple] done\n", 25)]);
            }

            return new StubStreamingPlan(AgentEnvelope("Generic worker completed."), [new StubOutputChunk("[generic]\n")]);
        };
    }

    private void ConfigureComplexExecutionWithRepair()
    {
        var buildAttempt = 0;
        var agentRequestCount = 0;

        Factory.ContainerManager.ExecHandler = invocation =>
        {
            if (!invocation.IsStreaming && invocation.Request?.FileName == "stub-agent")
            {
                var prompt = TryGetRequestArg(invocation.Request, "--prompt") ?? "";
                if (prompt.Contains("Decide whether this task should route into a browser-based website audit workflow.", StringComparison.Ordinal))
                    return WebsiteRoutingEnvelope(false, 9, "This is an architecture/code task, not a website audit.");

                agentRequestCount++;
                return agentRequestCount switch
                {
                    1 => AgentEnvelope("""{"complexity":9,"category":"architecture","recommended_model_power":1,"needs_decomposition":true}"""),
                    2 => AgentEnvelope("""["Create orchestration service","Add repair tests"]"""),
                    _ => Factory.ContainerManager.DefaultExecResult
                };
            }

            if (IsCompileProbe(invocation.Command))
                return new ExecResult(0, "MagicPAI.Server.csproj", "");
            if (IsTestProbe(invocation.Command))
                return new ExecResult(0, "MagicPAI.Tests.csproj", "");
            if (IsBuildCommand(invocation.Command))
            {
                buildAttempt++;
                return buildAttempt == 1
                    ? new ExecResult(1, "", "Compilation failed")
                    : new ExecResult(0, "Build succeeded", "");
            }
            if (IsTestCommand(invocation.Command))
                return new ExecResult(0, "Passed", "");
            if (IsFileListCommand(invocation.Command))
                return new ExecResult(0, "./MagicPAI.Server/Program.cs\n./MagicPAI.Server/MagicPAI.Server.csproj", "");
            if (IsSourceScanCommand(invocation.Command))
                return new ExecResult(0, "", "");

            return Factory.ContainerManager.DefaultExecResult;
        };

        Factory.ContainerManager.StreamingHandler = invocation =>
        {
            var prompt = TryGetRequestArg(invocation.Request, "--prompt") ?? "";

            if (prompt.Contains("Complex integration task", StringComparison.Ordinal))
            {
                return new StubStreamingPlan(
                    AgentEnvelope("Complex worker completed."),
                    [new StubOutputChunk("[complex] planning\n"), new StubOutputChunk("[complex] coding\n", 25)]);
            }

            if (prompt.Contains("The previous attempt had verification failures", StringComparison.Ordinal))
            {
                return new StubStreamingPlan(
                    AgentEnvelope("Repair applied successfully."),
                    [new StubOutputChunk("[repair] fixing\n"), new StubOutputChunk("[repair] retry ready\n", 25)]);
            }

            return new StubStreamingPlan(AgentEnvelope("Generic worker completed."), [new StubOutputChunk("[generic]\n")]);
        };
    }

    private void ConfigureStandardSimpleExecution()
    {
        var agentRequestCount = 0;
        Factory.ContainerManager.ExecHandler = invocation =>
        {
            if (!invocation.IsStreaming && invocation.Request?.FileName == "stub-agent")
            {
                agentRequestCount++;
                if (agentRequestCount == 1)
                    return AgentEnvelope("""{"complexity":2,"category":"docs","recommended_model_power":3,"needs_decomposition":false}""");
            }

            if (IsCompileProbe(invocation.Command))
                return new ExecResult(0, "MagicPAI.Server.csproj", "");
            if (IsTestProbe(invocation.Command))
                return new ExecResult(0, "MagicPAI.Tests.csproj", "");
            if (IsBuildCommand(invocation.Command))
                return new ExecResult(0, "Build succeeded", "");
            if (IsTestCommand(invocation.Command))
                return new ExecResult(0, "Passed", "");
            if (IsFileListCommand(invocation.Command))
                return new ExecResult(0, "./MagicPAI.Server/Program.cs\n./MagicPAI.Server/MagicPAI.Server.csproj", "");
            if (IsSourceScanCommand(invocation.Command))
                return new ExecResult(0, "", "");

            return Factory.ContainerManager.DefaultExecResult;
        };

        Factory.ContainerManager.StreamingHandler = invocation =>
        {
            var prompt = TryGetRequestArg(invocation.Request, "--prompt") ?? "";

            if (prompt.Contains("Improve this vague task", StringComparison.Ordinal))
            {
                return new StubStreamingPlan(
                    AgentEnvelope("Expanded prompt: break the work into explicit acceptance criteria."),
                    [new StubOutputChunk("[enhance] rewrite\n")]);
            }

            if (prompt.Contains("Expanded prompt:", StringComparison.Ordinal))
            {
                return new StubStreamingPlan(
                    AgentEnvelope("Context-aware prompt ready."),
                    [new StubOutputChunk("[context] gathered\n")]);
            }

            return new StubStreamingPlan(AgentEnvelope("Standard worker completed."), [new StubOutputChunk("[std]\n")]);
        };
    }

    private void ConfigureTriageFailureExecution()
    {
        Factory.ContainerManager.ExecHandler = invocation =>
        {
            if (!invocation.IsStreaming && invocation.Request?.FileName == "stub-agent")
                return new ExecResult(1, "", "triage exploded");

            return Factory.ContainerManager.DefaultExecResult;
        };
    }

    private void ConfigureLoopVerifierExecution()
    {
        var loopRuns = 0;

        Factory.ContainerManager.ExecHandler = invocation =>
        {
            if (!invocation.IsStreaming && invocation.Request?.FileName == "stub-agent")
            {
                if (loopRuns >= 2)
                    return AgentEnvelope("""{"complexity":2,"category":"testing","recommended_model_power":3,"needs_decomposition":false}""");

                return AgentEnvelope("""{"complexity":9,"category":"testing","recommended_model_power":3,"needs_decomposition":true}""");
            }

            return Factory.ContainerManager.DefaultExecResult;
        };

        Factory.ContainerManager.StreamingHandler = invocation =>
        {
            loopRuns++;
            return loopRuns == 1
                ? new StubStreamingPlan(
                    AgentEnvelope("Working on the task. More iteration is required."),
                    [new StubOutputChunk("[loop] first pass\n"), new StubOutputChunk("[loop] not done\n", 25)])
                : new StubStreamingPlan(
                    AgentEnvelope("Task complete. DONE."),
                    [new StubOutputChunk("[loop] second pass\n"), new StubOutputChunk("[loop] done\n", 25)]);
        };
    }

    private void ConfigureWebsiteAuditExecution()
    {
        var classifierCalls = 0;
        Factory.ContainerManager.ExecHandler = invocation =>
        {
            if (!invocation.IsStreaming && invocation.Request?.FileName == "stub-agent")
            {
                classifierCalls++;
                return classifierCalls switch
                {
                    1 => AgentEnvelope("""{"complexity":9,"category":"docs","recommended_model_power":3,"needs_decomposition":true}"""),
                    2 => AgentEnvelope("""{"complexity":2,"category":"docs","recommended_model_power":3,"needs_decomposition":false}"""),
                    3 => AgentEnvelope("""{"complexity":2,"category":"docs","recommended_model_power":3,"needs_decomposition":false}"""),
                    4 => AgentEnvelope("""{"complexity":2,"category":"testing","recommended_model_power":3,"needs_decomposition":false}"""),
                    _ => AgentEnvelope("""{"complexity":2,"category":"testing","recommended_model_power":3,"needs_decomposition":false}""")
                };
            }

            return Factory.ContainerManager.DefaultExecResult;
        };

        var discoveryRuns = 0;
        Factory.ContainerManager.StreamingHandler = invocation =>
        {
            var prompt = TryGetRequestArg(invocation.Request, "--prompt") ?? "";

            if (prompt.Contains("Phase 1: Discovery", StringComparison.Ordinal))
            {
                discoveryRuns++;
                return discoveryRuns == 1
                    ? new StubStreamingPlan(
                        AgentEnvelope("Discovery pass 1. Need more crawling before the discovery phase is complete."),
                        [new StubOutputChunk("[audit] discovery-1\n")])
                    : new StubStreamingPlan(
                        AgentEnvelope("Discovery pass 2. DISCOVERY_DONE. Key pages mapped."),
                        [new StubOutputChunk("[audit] discovery-2\n")]);
            }

            if (prompt.Contains("Phase 2: Visual audit", StringComparison.Ordinal))
            {
                return new StubStreamingPlan(
                    AgentEnvelope("Visual review complete. VISUAL_DONE. Found spacing and hierarchy issues."),
                    [new StubOutputChunk("[audit] visual\n")]);
            }

            if (prompt.Contains("Phase 3: Interaction and scroll audit", StringComparison.Ordinal))
            {
                return new StubStreamingPlan(
                    AgentEnvelope("Interaction review complete. INTERACTION_DONE. Found form validation issues."),
                    [new StubOutputChunk("[audit] interaction\n")]);
            }

            if (prompt.Contains("Phase 4: Final re-verification and synthesis", StringComparison.Ordinal))
            {
                return new StubStreamingPlan(
                    AgentEnvelope("Final website audit report with critical and medium issues."),
                    [new StubOutputChunk("[audit] final-report\n")]);
            }

            return new StubStreamingPlan(AgentEnvelope("Generic worker completed."), [new StubOutputChunk("[generic]\n")]);
        };
    }

    private void ConfigureWebsiteAuditExecutionWithScopedSessions()
    {
        var classifierCalls = 0;
        Factory.ContainerManager.ExecHandler = invocation =>
        {
            if (!invocation.IsStreaming && invocation.Request?.FileName == "stub-agent")
            {
                classifierCalls++;
                return classifierCalls switch
                {
                    1 => AgentEnvelope("""{"complexity":9,"category":"docs","recommended_model_power":3,"needs_decomposition":true}""", sessionId: "discovery-check-session"),
                    2 => AgentEnvelope("""{"complexity":2,"category":"docs","recommended_model_power":3,"needs_decomposition":false}""", sessionId: "discovery-check-session"),
                    3 => AgentEnvelope("""{"complexity":2,"category":"docs","recommended_model_power":3,"needs_decomposition":false}""", sessionId: "visual-check-session"),
                    4 => AgentEnvelope("""{"complexity":2,"category":"testing","recommended_model_power":3,"needs_decomposition":false}""", sessionId: "interaction-check-session"),
                    _ => AgentEnvelope("""{"complexity":2,"category":"testing","recommended_model_power":3,"needs_decomposition":false}""", sessionId: "interaction-check-session")
                };
            }

            return Factory.ContainerManager.DefaultExecResult;
        };

        var discoveryRuns = 0;
        Factory.ContainerManager.StreamingHandler = invocation =>
        {
            var prompt = TryGetRequestArg(invocation.Request, "--prompt") ?? "";

            if (prompt.Contains("Phase 1: Discovery", StringComparison.Ordinal))
            {
                discoveryRuns++;
                return discoveryRuns == 1
                    ? new StubStreamingPlan(
                        AgentEnvelope("Discovery pass 1. Need more crawling before the discovery phase is complete.", sessionId: "discovery-session"),
                        [new StubOutputChunk("[audit] discovery-1\n")])
                    : new StubStreamingPlan(
                        AgentEnvelope("Discovery pass 2. DISCOVERY_DONE. Key pages mapped.", sessionId: "discovery-session"),
                        [new StubOutputChunk("[audit] discovery-2\n")]);
            }

            if (prompt.Contains("Phase 2: Visual audit", StringComparison.Ordinal))
            {
                return new StubStreamingPlan(
                    AgentEnvelope("Visual review complete. VISUAL_DONE. Found spacing and hierarchy issues.", sessionId: "visual-session"),
                    [new StubOutputChunk("[audit] visual\n")]);
            }

            if (prompt.Contains("Phase 3: Interaction and scroll audit", StringComparison.Ordinal))
            {
                return new StubStreamingPlan(
                    AgentEnvelope("Interaction review complete. INTERACTION_DONE. Found form validation issues.", sessionId: "interaction-session"),
                    [new StubOutputChunk("[audit] interaction\n")]);
            }

            if (prompt.Contains("Phase 4: Final re-verification and synthesis", StringComparison.Ordinal))
            {
                return new StubStreamingPlan(
                    AgentEnvelope("Final website audit report with critical and medium issues.", sessionId: "final-session"),
                    [new StubOutputChunk("[audit] final-report\n")]);
            }

            return new StubStreamingPlan(AgentEnvelope("Generic worker completed."), [new StubOutputChunk("[generic]\n")]);
        };
    }

    private void ConfigureWebsiteFullOrchestrateExecution()
    {
        var triageCalls = 0;
        var discoveryRuns = 0;

        Factory.ContainerManager.ExecHandler = invocation =>
        {
            if (!invocation.IsStreaming && invocation.Request?.FileName == "stub-agent")
            {
                var prompt = TryGetRequestArg(invocation.Request, "--prompt") ?? "";
                if (prompt.Contains("Decide whether this task should route into a browser-based website audit workflow.", StringComparison.Ordinal))
                {
                    return prompt.Contains("IMPORTANT: CSS/UI/UX fix tasks ALWAYS route to WEBSITE AUDIT (true).", StringComparison.Ordinal)
                        ? WebsiteRoutingEnvelope(true, 10, "This is browser-visible CSS/UI work and must be audited in the browser.")
                        : WebsiteRoutingEnvelope(false, 1, "Missing CSS/UI website-audit routing rule.");
                }

                triageCalls++;
                return triageCalls switch
                {
                    1 => AgentEnvelope("""{"complexity":3,"category":"frontend","recommended_model_power":3,"needs_decomposition":false}"""),
                    2 => AgentEnvelope("""{"complexity":9,"category":"docs","recommended_model_power":3,"needs_decomposition":true}"""),
                    3 => AgentEnvelope("""{"complexity":2,"category":"docs","recommended_model_power":3,"needs_decomposition":false}"""),
                    4 => AgentEnvelope("""{"complexity":2,"category":"testing","recommended_model_power":3,"needs_decomposition":false}"""),
                    _ => AgentEnvelope("""{"complexity":2,"category":"testing","recommended_model_power":3,"needs_decomposition":false}""")
                };
            }

            if (IsCompileProbe(invocation.Command))
                return new ExecResult(0, "IsraelRise.Web.csproj", "");
            if (IsTestProbe(invocation.Command))
                return new ExecResult(0, "IsraelRise.Tests.csproj", "");
            if (IsBuildCommand(invocation.Command))
                return new ExecResult(0, "Build succeeded", "");
            if (IsTestCommand(invocation.Command))
                return new ExecResult(0, "Passed", "");
            if (IsFileListCommand(invocation.Command))
                return new ExecResult(0, "./src/IsraelRise.Web/Program.cs\n./src/IsraelRise.Web/wwwroot/css/tenants/ramathayal.css", "");
            if (IsSourceScanCommand(invocation.Command))
                return new ExecResult(0, "", "");

            return Factory.ContainerManager.DefaultExecResult;
        };

        Factory.ContainerManager.StreamingHandler = invocation =>
        {
            var prompt = TryGetRequestArg(invocation.Request, "--prompt") ?? "";

            if (prompt.Contains("yochange the entire css of ramathayal770", StringComparison.Ordinal) &&
                !prompt.Contains("Phase 1:", StringComparison.Ordinal))
            {
                return new StubStreamingPlan(
                    AgentEnvelope("Implemented the tenant CSS redesign and verified the changed pages."),
                    [new StubOutputChunk("[website] redesign\n"), new StubOutputChunk("[website] verify\n", 25)]);
            }

            if (prompt.Contains("Phase 1: Discovery", StringComparison.Ordinal))
            {
                discoveryRuns++;
                return discoveryRuns == 1
                    ? new StubStreamingPlan(
                        AgentEnvelope("Discovery pass 1. Need one more navigation sweep before the discovery phase is complete."),
                        [new StubOutputChunk("[audit] discovery-1\n")])
                    : new StubStreamingPlan(
                        AgentEnvelope("Discovery pass 2. DISCOVERY_DONE. Key tenant pages and donation flow are mapped."),
                        [new StubOutputChunk("[audit] discovery-2\n")]);
            }

            if (prompt.Contains("Phase 2: Visual audit", StringComparison.Ordinal))
            {
                return new StubStreamingPlan(
                    AgentEnvelope("Visual audit complete. Fixed spacing and contrast issues, rebuilt the site, and re-tested in Playwright. VISUAL_DONE."),
                    [new StubOutputChunk("[audit] visual-fix\n")]);
            }

            if (prompt.Contains("Phase 3: Interaction and scroll audit", StringComparison.Ordinal))
            {
                return new StubStreamingPlan(
                    AgentEnvelope("Interaction review complete. Fixed button state regression, re-ran the build, and confirmed the donation flow. INTERACTION_DONE."),
                    [new StubOutputChunk("[audit] interaction-fix\n")]);
            }

            if (prompt.Contains("Phase 4: Final re-verification and synthesis", StringComparison.Ordinal))
            {
                return new StubStreamingPlan(
                    AgentEnvelope("Final re-verification completed. High-risk pages were re-checked and no blocking regressions remain."),
                    [new StubOutputChunk("[audit] final-sweep\n")]);
            }

            return new StubStreamingPlan(AgentEnvelope("Generic worker completed."), [new StubOutputChunk("[generic]\n")]);
        };
    }

    private async Task<string> CreateSessionWithRetryAsync(string prompt, string workflowName)
    {
        string? lastFailure = null;

        for (var attempt = 0; attempt < 20; attempt++)
        {
            var response = await Client.PostAsJsonAsync("/api/sessions", new CreateSessionRequest(
                Prompt: prompt,
                WorkspacePath: "C:/AllGit/CSharp/MagicPAI",
                AiAssistant: "claude",
                Agent: "claude",
                Model: "auto",
                WorkflowName: workflowName));

            if (response.StatusCode == HttpStatusCode.OK)
            {
                var payload = await response.Content.ReadFromJsonAsync<CreateSessionResponse>();
                return payload!.SessionId;
            }

            lastFailure = $"{(int)response.StatusCode} {response.StatusCode}: {await response.Content.ReadAsStringAsync()}";

            await Task.Delay(1000);
        }

        throw new TimeoutException($"Could not create session for workflow '{workflowName}'. Last failure: {lastFailure}");
    }

    private async Task<SessionInfo> WaitForTerminalStateAsync(string sessionId)
    {
        SessionInfo? lastSession = null;

        for (var attempt = 0; attempt < 60; attempt++)
        {
            var session = await Client.GetFromJsonAsync<SessionInfo>($"/api/sessions/{sessionId}");
            lastSession = session;
            if (session is not null && session.State is "completed" or "failed" or "cancelled")
                return session;

            await Task.Delay(500);
        }

        var activities = await Client.GetFromJsonAsync<List<ActivityState>>($"/api/sessions/{sessionId}/activities") ?? [];
        var logs = await GetExecutionLogsAsync(sessionId);
        throw new TimeoutException(
            $"Session '{sessionId}' did not reach a terminal state. Last state: '{lastSession?.State}'. Activities: {string.Join(", ", activities.Select(x => $"{x.Name}:{x.Status}"))}. Logs: {string.Join(" | ", logs.Select(x => $"{x.EventName}:{x.Message}"))}");
    }

    private async Task<List<ActivityState>> WaitForActivitiesAsync(string sessionId, params string[] expectedActivityNames)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            var activities = await Client.GetFromJsonAsync<List<ActivityState>>($"/api/sessions/{sessionId}/activities") ?? [];
            if (expectedActivityNames.All(name => activities.Any(x => string.Equals(x.Name, name, StringComparison.Ordinal))))
                return activities;

            await Task.Delay(500);
        }

        return await Client.GetFromJsonAsync<List<ActivityState>>($"/api/sessions/{sessionId}/activities") ?? [];
    }

    private async Task<List<ExecutionLogRecord>> WaitForExecutionLogsAsync(string sessionId, params string[] expectedEventNames)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var logs = await GetExecutionLogsAsync(sessionId);
            if (expectedEventNames.All(name => logs.Any(x => string.Equals(x.EventName, name, StringComparison.Ordinal))))
                return logs;

            await Task.Delay(250);
        }

        return await GetExecutionLogsAsync(sessionId);
    }

    private async Task<List<ExecutionLogRecord>> WaitForExecutionLogCountAsync(string sessionId, string eventName, int expectedCount)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var logs = await GetExecutionLogsAsync(sessionId);
            if (logs.Count(x => string.Equals(x.EventName, eventName, StringComparison.Ordinal)) >= expectedCount)
                return logs;

            await Task.Delay(250);
        }

        return await GetExecutionLogsAsync(sessionId);
    }

    private async Task<List<ExecutionLogRecord>> GetExecutionLogsAsync(string sessionId)
    {
        using var scope = Factory.Services.CreateScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var connectionString = configuration.GetConnectionString("MagicPai")
            ?? throw new InvalidOperationException("Missing MagicPai connection string in integration test host.");

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            WITH RECURSIVE workflow_tree AS (
                SELECT "Id"
                FROM "Elsa"."WorkflowInstances"
                WHERE "Id" = @id
                UNION ALL
                SELECT child."Id"
                FROM "Elsa"."WorkflowInstances" child
                INNER JOIN workflow_tree parent ON child."ParentWorkflowInstanceId" = parent."Id"
            )
            SELECT logs."EventName", logs."Message"
            FROM "Elsa"."WorkflowExecutionLogRecords" logs
            INNER JOIN workflow_tree tree ON tree."Id" = logs."WorkflowInstanceId"
            ORDER BY "Timestamp" ASC, "Sequence" ASC;
            """;
        cmd.Parameters.AddWithValue("id", sessionId);

        var logs = new List<ExecutionLogRecord>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            logs.Add(new ExecutionLogRecord(
                reader.IsDBNull(0) ? "" : reader.GetString(0),
                reader.IsDBNull(1) ? "" : reader.GetString(1)));
        }

        return logs;
    }

    private async Task WaitForDestroyedContainersAsync(int expectedCount)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (Factory.ContainerManager.DestroyedContainers.Count >= expectedCount)
                return;

            await Task.Delay(250);
        }
    }

    private static ExecResult AgentEnvelope(string output, bool success = true, string? sessionId = null) =>
        new(0, JsonSerializer.Serialize(new
        {
            Success = success,
            Output = output,
            CostUsd = 0.01m,
            FilesModified = Array.Empty<string>(),
            InputTokens = 100,
            OutputTokens = 50,
            SessionId = sessionId
        }), "");

    private static ExecResult WebsiteRoutingEnvelope(bool isWebsiteTask, int confidence, string rationale) =>
        AgentEnvelope(JsonSerializer.Serialize(new
        {
            isWebsiteTask,
            confidence,
            rationale
        }));

    private static string? TryGetRequestArg(ContainerExecRequest? request, string name)
    {
        if (request is null)
            return null;

        for (var i = 0; i < request.Arguments.Count - 1; i++)
        {
            if (string.Equals(request.Arguments[i], name, StringComparison.Ordinal))
                return request.Arguments[i + 1];
        }

        return null;
    }

    private static bool IsCompileProbe(string? command) =>
        command?.Contains("ls *.csproj", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsTestProbe(string? command) =>
        command?.Contains("find . -maxdepth 3", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsBuildCommand(string? command) =>
        command?.Contains("dotnet build", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsTestCommand(string? command) =>
        command?.Contains("dotnet test", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsFileListCommand(string? command) =>
        command?.Contains("find . -type f -not -path", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsSourceScanCommand(string? command) =>
        command?.Contains("grep -n -H -E", StringComparison.OrdinalIgnoreCase) == true;

    private sealed record ExecutionLogRecord(string EventName, string Message);
}
