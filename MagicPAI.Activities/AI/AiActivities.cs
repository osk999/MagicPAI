using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Temporalio.Activities;
using Temporalio.Exceptions;
using MagicPAI.Activities.Contracts;
using MagicPAI.Core.Config;
using MagicPAI.Core.Models;
using MagicPAI.Core.Services;
using MagicPAI.Core.Services.Auth;

namespace MagicPAI.Activities.AI;

/// <summary>
/// Temporal activity group for AI CLI agent orchestration.
/// Day 3 scope: <see cref="RunCliAgentAsync"/>.
/// Day 4 adds <see cref="TriageAsync"/>, <see cref="ClassifyAsync"/>,
/// <see cref="RouteModelAsync"/>, <see cref="EnhancePromptAsync"/>.
/// Day 5+ adds Architect, ResearchPrompt, ClassifyWebsiteTask, GradeCoverage.
/// See temporal.md §7.8 and §I.1 for the full spec.
/// </summary>
/// <remarks>
/// <para>
/// The reference template in temporal.md §7.8 assumes
/// <c>IContainerManager.ExecStreamingAsync</c> returns an
/// <see cref="IAsyncEnumerable{T}"/> of lines. The real MagicPAI API is
/// callback-based (<c>onOutput</c> + <see cref="TimeSpan"/> timeout), identical
/// to the shape already handled in <see cref="Docker.DockerActivities.StreamAsync"/>.
/// We re-use that adaptation pattern here: split chunks by newline in the callback,
/// run line accounting / heartbeating / SignalR fan-out inside the callback, and
/// let <see cref="IContainerManager.ExecStreamingAsync(string, ContainerExecRequest, Action{string}, TimeSpan, CancellationToken)"/>
/// own the process lifecycle (including cancellation).
/// </para>
/// <para>
/// Auth recovery: if the raw output matches known auth-error patterns
/// (see <see cref="AuthErrorDetector"/>), we attempt credential refresh via
/// <see cref="AuthRecoveryService"/> and inject the fresh credentials into the
/// container. We then throw a <i>retryable</i> <see cref="ApplicationFailureException"/>
/// of type <c>"AuthRefreshed"</c> so Temporal schedules another attempt with
/// the new credentials. If recovery fails, we throw a non-retryable
/// <c>"AuthError"</c> so the workflow fails fast (retrying would just fail again).
/// </para>
/// </remarks>
public class AiActivities
{
    private readonly ICliAgentFactory _factory;
    private readonly IContainerManager _docker;
    private readonly ISessionStreamSink _sink;
    private readonly AuthRecoveryService _auth;
    private readonly MagicPaiConfig _config;
    private readonly ILogger<AiActivities> _log;

    public AiActivities(
        ICliAgentFactory factory,
        IContainerManager docker,
        ISessionStreamSink sink,
        AuthRecoveryService auth,
        MagicPaiConfig config,
        ILogger<AiActivities>? log = null)
    {
        _factory = factory;
        _docker = docker;
        _sink = sink;
        _auth = auth;
        _config = config;
        _log = log ?? NullLogger<AiActivities>.Instance;
    }

    [Activity]
    public async Task<RunCliAgentOutput> RunCliAgentAsync(RunCliAgentInput input)
    {
        var ctx = ActivityExecutionContext.Current;
        var ct = ctx.CancellationToken;

        // Resume marker: on retry, skip output we already streamed.
        var resumeOffset = 0;
        if (ctx.Info.HeartbeatDetails.Count > 0)
        {
            try
            {
                resumeOffset = await ctx.Info.HeartbeatDetailAtAsync<int>(0);
            }
            catch
            {
                resumeOffset = 0;
            }
        }

        var assistantName = AiAssistantResolver.NormalizeAssistant(
            input.AiAssistant, _config.DefaultAgent);
        var runner = _factory.Create(assistantName);
        var resolvedModel = ResolveModel(input, runner);

        var request = new AgentRequest
        {
            Prompt = input.Prompt,
            Model = resolvedModel,
            OutputSchema = string.IsNullOrWhiteSpace(input.StructuredOutputSchema)
                ? null : input.StructuredOutputSchema,
            WorkDir = NormalizeContainerWorkDir(input.WorkingDirectory),
            // CLI session ID is a CLI-provider UUID (Claude/Codex/Gemini), NOT the MagicPAI workflow ID.
            // Only pass it when the workflow has received one from a prior activity invocation.
            SessionId = input.AssistantSessionId,
        };

        var plan = runner.BuildExecutionPlan(request);

        // Run any one-time setup commands (e.g., workspace init) before the main call.
        foreach (var setup in plan.SetupRequests ?? [])
        {
            var setupResult = await _docker.ExecAsync(input.ContainerId, setup, ct);
            if (setupResult.ExitCode != 0)
                throw new ApplicationFailureException(
                    $"Failed to prepare assistant execution: {setupResult.Error}",
                    errorType: "SetupError",
                    nonRetryable: false);
        }

        var lineCount = 0;
        var captured = new StringBuilder();
        var timeout = TimeSpan.FromMinutes(Math.Max(1, input.InactivityTimeoutMinutes));

        void OnOutput(string chunk)
        {
            // Callback surface: chunks may hold multiple lines. We split by \n and
            // perform heartbeating / sink fan-out per line, mirroring the temporal.md
            // §7.8 template (which assumes an IAsyncEnumerable<string> per line).
            foreach (var line in chunk.Split('\n'))
            {
                if (string.IsNullOrEmpty(line)) continue;
                lineCount++;
                if (lineCount <= resumeOffset) continue;

                captured.AppendLine(line);

                if (input.SessionId is not null)
                {
                    // Fire-and-forget: SignalR emit is best-effort. Do not fail the
                    // activity if the sink is offline; the line still counts toward
                    // the captured buffer and the heartbeat offset.
                    try
                    {
                        _sink.EmitChunkAsync(input.SessionId, line, ct)
                            .GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        _log.LogDebug(ex, "Sink emit failed for {SessionId}", input.SessionId);
                    }
                }

                if (lineCount % 20 == 0)
                    ctx.Heartbeat(lineCount);
            }
        }

        // Background heartbeat ticker — pings Temporal every 30 s regardless of
        // whether the CLI is emitting output. LLMs can legitimately go quiet
        // for minutes during thinking blocks, long file writes, or WebFetch
        // tool loops; without this ticker the per-line `ctx.Heartbeat` alone
        // can trip the HeartbeatTimeout (5 min) on deeply quiet stretches.
        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var heartbeatTask = Task.Run(async () =>
        {
            try
            {
                while (!heartbeatCts.Token.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), heartbeatCts.Token);
                    ctx.Heartbeat(lineCount);
                }
            }
            catch (OperationCanceledException) { /* expected on teardown */ }
        }, heartbeatCts.Token);

        ExecResult result;
        try
        {
            result = await _docker.ExecStreamingAsync(
                input.ContainerId,
                plan.MainRequest,
                OnOutput,
                timeout,
                ct);
        }
        catch (OperationCanceledException)
        {
            _log.LogInformation("RunCliAgent cancelled at line {Line}", lineCount);
            throw;
        }
        finally
        {
            heartbeatCts.Cancel();
            try { await heartbeatTask; } catch { /* ignore shutdown noise */ }
        }

        var raw = captured.ToString();
        // Fall back to ExecResult.Output if the callback did not observe any chunks
        // (some container managers may only report on stream close).
        if (raw.Length == 0 && !string.IsNullOrEmpty(result.Output))
            raw = result.Output;

        // Auth recovery path — detect → recover → inject → retry via Temporal.
        if (AuthErrorDetector.ContainsAuthError(raw)
            || AuthErrorDetector.ContainsAuthError(result.Error))
        {
            _log.LogWarning("Auth error detected; attempting credential recovery");
            var (recovered, authError, credsJson) = await _auth.RecoverAuthAsync(ct);
            if (recovered && !string.IsNullOrEmpty(credsJson))
            {
                await CredentialInjector.InjectAsync(input.ContainerId, credsJson, ct);
                // Retryable: Temporal will re-run the activity with fresh creds.
                throw new ApplicationFailureException(
                    "Auth recovered; retrying",
                    errorType: "AuthRefreshed",
                    nonRetryable: false);
            }

            // When no AuthServiceUrl is configured we have no way to refresh
            // credentials — but the "auth error pattern" detection is heuristic
            // and often fires on Claude's own narration of auth concepts (false
            // positives). Rather than fail the whole workflow non-retryably,
            // log a warning and let the output flow through as-is. The operator
            // can configure AuthServiceUrl in production for true expired-token
            // handling; in dev we don't want a spurious content match to abort.
            if (string.Equals(authError, "AuthServiceUrl not configured", StringComparison.Ordinal))
            {
                _log.LogWarning(
                    "Auth-like pattern in CLI output but AuthServiceUrl not configured; treating as content, not failure. Session={SessionId}",
                    input.SessionId);
                // fall through to normal parse path below
            }
            else
            {
                // Non-retryable: retrying without fresh creds will just fail again.
                throw new ApplicationFailureException(
                    $"Auth recovery failed: {authError ?? "no details"}",
                    errorType: "AuthError",
                    nonRetryable: true);
            }
        }

        // Parse the raw response. Keep parse failures soft — we still return the
        // raw output to the caller so downstream can inspect/log it.
        CliAgentResponse parsed;
        try
        {
            parsed = runner.ParseResponse(raw);
        }
        catch (Exception parseEx)
        {
            _log.LogWarning(parseEx, "ParseResponse threw; returning raw output");
            parsed = new CliAgentResponse(
                Success: false,
                Output: raw,
                CostUsd: 0m,
                FilesModified: Array.Empty<string>(),
                InputTokens: 0,
                OutputTokens: 0,
                SessionId: null);
        }

        var response = !string.IsNullOrWhiteSpace(parsed.Output)
            ? parsed.Output
            : raw;

        // Cap the return-value Response at 8 KB. The full raw output is already
        // streamed to SignalR chunk-by-chunk via ISessionStreamSink during the
        // run; Temporal's event history doesn't need it and a multi-hundred-KB
        // activity return value bloats history across replays and visibility.
        // The head of the output is the "final assistant message" which is the
        // actionable part; everything before it is tool_use/stream_event
        // framing that the SignalR consumer already received live.
        const int MaxHistoryBytes = 8 * 1024;
        var truncatedResponse = TruncateForHistory(response, MaxHistoryBytes);

        return new RunCliAgentOutput(
            Response: truncatedResponse,
            StructuredOutputJson: parsed.StructuredOutputJson,
            Success: result.ExitCode == 0 && parsed.Success,
            CostUsd: parsed.CostUsd,
            InputTokens: parsed.InputTokens,
            OutputTokens: parsed.OutputTokens,
            FilesModified: parsed.FilesModified ?? Array.Empty<string>(),
            ExitCode: result.ExitCode,
            AssistantSessionId: parsed.SessionId);
    }

    /// <summary>
    /// Truncates a response string to fit comfortably in Temporal history.
    /// Keeps the tail (last N bytes) because the final model message usually
    /// lives at the end of the stream-json output; prepends a marker so
    /// consumers know the truncation happened.
    /// </summary>
    internal static string TruncateForHistory(string? value, int maxBytes)
    {
        if (string.IsNullOrEmpty(value)) return value ?? "";
        var utf8 = System.Text.Encoding.UTF8;
        var bytes = utf8.GetByteCount(value);
        if (bytes <= maxBytes) return value;

        // Byte-window of tail to keep; floor at 1024 bytes so the result is
        // still useful for short maxBytes. Reserve ~256 bytes for the header.
        var targetTailBytes = Math.Max(1024, maxBytes - 256);

        // Walk the string from the end and accumulate chars until the tail
        // reaches ~targetTailBytes. This works for multi-byte UTF-8 (e.g. emoji,
        // accented chars) where char-count and byte-count diverge.
        var tailBytes = 0;
        var takeFrom = value.Length;
        while (takeFrom > 0)
        {
            var ch = value[takeFrom - 1];
            // Approximate UTF-8 byte size: ASCII=1, BMP non-ASCII=~3, surrogate pair=~4.
            var chBytes = ch < 0x80 ? 1 : ch < 0x800 ? 2 : 3;
            if (tailBytes + chBytes > targetTailBytes) break;
            tailBytes += chBytes;
            takeFrom--;
        }
        var tail = value[takeFrom..];
        var header = $"[truncated {bytes} bytes → keeping last {utf8.GetByteCount(tail)}]\n";

        // Paranoia: if the header+tail somehow exceeds the original byte count
        // (happens for very-multi-byte input with tiny maxBytes), return the
        // input unchanged — never make the payload larger than the source.
        var resultBytes = utf8.GetByteCount(header) + utf8.GetByteCount(tail);
        if (resultBytes >= bytes) return value;
        return header + tail;
    }

    /// <summary>
    /// Day 4 — classify a coding task's complexity (1-10), bucket it into a category,
    /// and recommend a model power. Runs under a cheap model (power=3) by default.
    /// Falls back to a deterministic heuristic when the agent fails or returns
    /// unparsable JSON, so the caller can always proceed. temporal.md §I.1.
    /// </summary>
    [Activity]
    public async Task<TriageOutput> TriageAsync(TriageInput input)
    {
        if (string.IsNullOrWhiteSpace(input.ContainerId))
            throw new ApplicationFailureException(
                "Triage requires a ContainerId; AI agents always run inside a worker container.",
                errorType: "ConfigError", nonRetryable: true);

        var ct = ActivityExecutionContext.Current.CancellationToken;

        var assistantName = AiAssistantResolver.NormalizeAssistant(input.AiAssistant, _config.DefaultAgent);
        var runner = _factory.Create(assistantName);
        var triagePrompt = BuildTriagePrompt(input.Prompt, input.ClassificationInstructions);
        var schema = SchemaGenerator.FromType<TriageResult>();

        var request = new AgentRequest
        {
            Prompt = triagePrompt,
            Model = AiAssistantResolver.ResolveModelForPower(runner, _config, 3),
            OutputSchema = schema,
            WorkDir = _config.ContainerWorkDir ?? "/workspace",
            SessionId = null /* fresh Claude session — continuity only in RunCliAgentAsync */
        };

        var plan = runner.BuildExecutionPlan(request);

        foreach (var setup in plan.SetupRequests ?? [])
            await _docker.ExecAsync(input.ContainerId, setup, ct);

        TriageResult parsed;
        try
        {
            var result = await _docker.ExecAsync(input.ContainerId, plan.MainRequest, ct);

            // Auth recovery: retry once if auth error detected on the first attempt.
            if (result.ExitCode != 0
                && AuthErrorDetector.ContainsAuthError((result.Output ?? "") + (result.Error ?? "")))
            {
                _log.LogWarning("Triage auth error detected; attempting credential recovery");
                var (recovered, authError, credsJson) = await _auth.RecoverAuthAsync(ct);
                if (recovered && !string.IsNullOrEmpty(credsJson))
                {
                    await CredentialInjector.InjectAsync(input.ContainerId, credsJson, ct);
                    result = await _docker.ExecAsync(input.ContainerId, plan.MainRequest, ct);
                }
                else
                {
                    _log.LogWarning("Triage auth recovery failed: {Error}", authError);
                }
            }

            if (result.ExitCode != 0)
            {
                _log.LogWarning(
                    "Triage agent exited with code {ExitCode}; falling back. Error: {Error}",
                    result.ExitCode, Truncate(result.Error));
                parsed = FallbackTriageResult(input.Prompt);
            }
            else
            {
                var parsedResp = runner.ParseResponse(result.Output ?? "");
                parsed = ParseTriageJson(parsedResp.Output ?? result.Output ?? "");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Triage threw; falling back");
            parsed = FallbackTriageResult(input.Prompt);
        }

        var recommendedModel = AiAssistantResolver.ResolveModelForPower(
            runner, _config, parsed.RecommendedModelPower);
        var isComplex = parsed.Complexity >= input.ComplexityThreshold;

        return new TriageOutput(
            Complexity: parsed.Complexity,
            Category: parsed.Category,
            RecommendedModel: recommendedModel,
            RecommendedModelPower: parsed.RecommendedModelPower,
            NeedsDecomposition: parsed.NeedsDecomposition,
            IsComplex: isComplex);
    }

    /// <summary>
    /// Day 4 — binary classifier: asks the agent a yes/no question about the
    /// prompt and returns { result, confidence, rationale }. Falls back to
    /// { false, 0, "parse-failure" } on any error. temporal.md §I.1.
    /// </summary>
    [Activity]
    public async Task<ClassifierOutput> ClassifyAsync(ClassifierInput input)
    {
        if (string.IsNullOrWhiteSpace(input.ContainerId))
            throw new ApplicationFailureException(
                "Classify requires a ContainerId; AI agents always run inside a worker container.",
                errorType: "ConfigError", nonRetryable: true);

        var ct = ActivityExecutionContext.Current.CancellationToken;

        var assistantName = AiAssistantResolver.NormalizeAssistant(input.AiAssistant, _config.DefaultAgent);
        var runner = _factory.Create(assistantName);
        var prompt = $"""
            Answer yes or no with rationale:
            Question: {input.ClassificationQuestion}
            Content: {input.Prompt}
            """;

        var schema = """
            {"type":"object","properties":{"result":{"type":"boolean"},"confidence":{"type":"number"},"rationale":{"type":"string"}},"required":["result","rationale"],"additionalProperties":false}
            """;

        var modelPower = input.ModelPower > 0 ? input.ModelPower : 3;

        var request = new AgentRequest
        {
            Prompt = prompt,
            Model = AiAssistantResolver.ResolveModelForPower(runner, _config, modelPower),
            OutputSchema = schema,
            WorkDir = _config.ContainerWorkDir ?? "/workspace",
            SessionId = null /* fresh Claude session — continuity only in RunCliAgentAsync */
        };

        var plan = runner.BuildExecutionPlan(request);

        foreach (var setup in plan.SetupRequests ?? [])
            await _docker.ExecAsync(input.ContainerId, setup, ct);

        ExecResult result;
        try
        {
            result = await _docker.ExecAsync(input.ContainerId, plan.MainRequest, ct);

            if (result.ExitCode != 0
                && AuthErrorDetector.ContainsAuthError((result.Output ?? "") + (result.Error ?? "")))
            {
                _log.LogWarning("Classify auth error detected; attempting credential recovery");
                var (recovered, authError, credsJson) = await _auth.RecoverAuthAsync(ct);
                if (recovered && !string.IsNullOrEmpty(credsJson))
                {
                    await CredentialInjector.InjectAsync(input.ContainerId, credsJson, ct);
                    result = await _docker.ExecAsync(input.ContainerId, plan.MainRequest, ct);
                }
                else
                {
                    _log.LogWarning("Classify auth recovery failed: {Error}", authError);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Classify threw; returning parse-failure fallback");
            return new ClassifierOutput(Result: false, Confidence: 0m, Rationale: "parse-failure");
        }

        try
        {
            var parsedResp = runner.ParseResponse(result.Output ?? "");
            var body = !string.IsNullOrWhiteSpace(parsedResp.StructuredOutputJson)
                ? parsedResp.StructuredOutputJson!
                : parsedResp.Output ?? result.Output ?? "{}";
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            return new ClassifierOutput(
                Result: root.TryGetProperty("result", out var r) && r.GetBoolean(),
                Confidence: root.TryGetProperty("confidence", out var c) ? (decimal)c.GetDouble() : 0.5m,
                Rationale: root.TryGetProperty("rationale", out var rat) ? rat.GetString() ?? "" : "");
        }
        catch
        {
            return new ClassifierOutput(Result: false, Confidence: 0m, Rationale: "parse-failure");
        }
    }

    /// <summary>
    /// Day 4 — pure CPU model router. No container required, no AI call. Maps
    /// (complexity, preferredAgent) to a concrete (agent, model) pair using
    /// the configured model-power map. temporal.md §I.1.
    /// </summary>
    [Activity]
    public Task<RouteModelOutput> RouteModelAsync(RouteModelInput input)
    {
        var agentName = !string.IsNullOrWhiteSpace(input.PreferredAgent)
            ? input.PreferredAgent!
            : _config.DefaultAgent;
        var normalized = AiAssistantResolver.NormalizeAssistant(agentName, _config.DefaultAgent);
        var power = input.Complexity switch
        {
            >= 8 => 1,      // opus / gpt-5.4 — strongest
            >= 4 => 2,      // sonnet / gpt-5.3-codex — balanced
            _    => 3       // haiku / gemini-flash — cheapest
        };
        var runner = _factory.Create(normalized);
        var model = AiAssistantResolver.ResolveModelForPower(runner, _config, power);
        return Task.FromResult(new RouteModelOutput(
            SelectedAgent: normalized,
            SelectedModel: model));
    }

    /// <summary>
    /// Day 4 — enhance / rewrite a prompt per the caller-supplied instructions.
    /// Returns the enhanced prompt plus a wasEnhanced flag. On parse failure,
    /// returns the original prompt with wasEnhanced=false. temporal.md §I.1.
    /// </summary>
    [Activity]
    public async Task<EnhancePromptOutput> EnhancePromptAsync(EnhancePromptInput input)
    {
        if (string.IsNullOrWhiteSpace(input.ContainerId))
            throw new ApplicationFailureException(
                "EnhancePrompt requires a ContainerId; AI agents always run inside a worker container.",
                errorType: "ConfigError", nonRetryable: true);

        var ct = ActivityExecutionContext.Current.CancellationToken;

        var assistantName = AiAssistantResolver.NormalizeAssistant(input.AiAssistant, _config.DefaultAgent);
        var runner = _factory.Create(assistantName);

        var enhancePrompt = $$"""
            {{input.EnhancementInstructions}}

            Original prompt:
            {{input.OriginalPrompt}}

            Return JSON only: {"enhancedPrompt": "...", "wasEnhanced": true|false, "rationale": "..."}
            """;

        const string schema = """
            {"type":"object","properties":{"enhancedPrompt":{"type":"string"},"wasEnhanced":{"type":"boolean"},"rationale":{"type":"string"}},"required":["enhancedPrompt","wasEnhanced","rationale"],"additionalProperties":false}
            """;

        var modelPower = input.ModelPower > 0 ? input.ModelPower : 2;

        var request = new AgentRequest
        {
            Prompt = enhancePrompt,
            Model = AiAssistantResolver.ResolveModelForPower(runner, _config, modelPower),
            OutputSchema = schema,
            WorkDir = _config.ContainerWorkDir ?? "/workspace",
            SessionId = null /* fresh Claude session — continuity only in RunCliAgentAsync */
        };

        var plan = runner.BuildExecutionPlan(request);

        foreach (var setup in plan.SetupRequests ?? [])
            await _docker.ExecAsync(input.ContainerId, setup, ct);

        ExecResult result;
        try
        {
            result = await _docker.ExecAsync(input.ContainerId, plan.MainRequest, ct);

            if (result.ExitCode != 0
                && AuthErrorDetector.ContainsAuthError((result.Output ?? "") + (result.Error ?? "")))
            {
                _log.LogWarning("EnhancePrompt auth error detected; attempting credential recovery");
                var (recovered, authError, credsJson) = await _auth.RecoverAuthAsync(ct);
                if (recovered && !string.IsNullOrEmpty(credsJson))
                {
                    await CredentialInjector.InjectAsync(input.ContainerId, credsJson, ct);
                    result = await _docker.ExecAsync(input.ContainerId, plan.MainRequest, ct);
                }
                else
                {
                    _log.LogWarning("EnhancePrompt auth recovery failed: {Error}", authError);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "EnhancePrompt threw; returning original prompt");
            return new EnhancePromptOutput(input.OriginalPrompt, WasEnhanced: false, Rationale: "parse-failure");
        }

        try
        {
            var parsedResp = runner.ParseResponse(result.Output ?? "");
            var body = !string.IsNullOrWhiteSpace(parsedResp.StructuredOutputJson)
                ? parsedResp.StructuredOutputJson!
                : parsedResp.Output ?? result.Output ?? "{}";
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var enhanced = root.TryGetProperty("enhancedPrompt", out var e)
                ? e.GetString() ?? input.OriginalPrompt
                : input.OriginalPrompt;
            var wasEnhanced = root.TryGetProperty("wasEnhanced", out var w) && w.GetBoolean();
            var rationale = root.TryGetProperty("rationale", out var rt) ? rt.GetString() : null;
            return new EnhancePromptOutput(enhanced, wasEnhanced, rationale);
        }
        catch
        {
            return new EnhancePromptOutput(input.OriginalPrompt, WasEnhanced: false, Rationale: "parse-failure");
        }
    }

    /// <summary>
    /// Day 5 — decompose a task into independent subtasks with file ownership.
    /// Runs on the strongest model (ModelPower=1) since planning quality impacts
    /// every downstream step. Returns a structured task list plus the raw JSON
    /// for workflows that want to route by it. temporal.md §I.1.
    /// </summary>
    [Activity]
    public async Task<ArchitectOutput> ArchitectAsync(ArchitectInput input)
    {
        if (string.IsNullOrWhiteSpace(input.ContainerId))
            throw new ApplicationFailureException(
                "Architect requires a ContainerId; AI agents always run inside a worker container.",
                errorType: "ConfigError", nonRetryable: true);

        var ct = ActivityExecutionContext.Current.CancellationToken;

        var assistantName = AiAssistantResolver.NormalizeAssistant(input.AiAssistant, _config.DefaultAgent);
        var runner = _factory.Create(assistantName);
        var architectPrompt = $$"""
            Decompose this task into independent subtasks with file ownership.
            Task: {{input.Prompt}}
            {{(string.IsNullOrWhiteSpace(input.GapContext) ? "" : $"Additional context:\n{input.GapContext}")}}
            Return JSON only:
            {
              "tasks": [
                { "id": "task-1", "description": "...", "dependsOn": [], "filesTouched": ["..."] }
              ]
            }
            """;

        var request = new AgentRequest
        {
            Prompt = architectPrompt,
            Model = AiAssistantResolver.ResolveModelForPower(runner, _config, 1),
            WorkDir = _config.ContainerWorkDir ?? "/workspace",
            SessionId = null /* fresh Claude session — continuity only in RunCliAgentAsync */
        };

        var plan = runner.BuildExecutionPlan(request);

        foreach (var setup in plan.SetupRequests ?? [])
            await _docker.ExecAsync(input.ContainerId, setup, ct);

        ExecResult result;
        try
        {
            result = await _docker.ExecAsync(input.ContainerId, plan.MainRequest, ct);

            if (result.ExitCode != 0
                && AuthErrorDetector.ContainsAuthError((result.Output ?? "") + (result.Error ?? "")))
            {
                _log.LogWarning("Architect auth error detected; attempting credential recovery");
                var (recovered, authError, credsJson) = await _auth.RecoverAuthAsync(ct);
                if (recovered && !string.IsNullOrEmpty(credsJson))
                {
                    await CredentialInjector.InjectAsync(input.ContainerId, credsJson, ct);
                    result = await _docker.ExecAsync(input.ContainerId, plan.MainRequest, ct);
                }
                else
                {
                    _log.LogWarning("Architect auth recovery failed: {Error}", authError);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Architect threw; returning empty plan");
            return new ArchitectOutput(
                TaskListJson: "[]",
                TaskCount: 0,
                Tasks: Array.Empty<TaskPlanEntry>());
        }

        var parsedResp = runner.ParseResponse(result.Output ?? "");
        var body = !string.IsNullOrWhiteSpace(parsedResp.StructuredOutputJson)
            ? parsedResp.StructuredOutputJson!
            : parsedResp.Output ?? result.Output ?? "";
        var tasks = ParseTasks(body);

        // Surface zero-task results loudly. The Architect's silent-empty
        // fallback exists to protect the workflow from parse crashes, but a
        // zero-task plan is almost always a bug (misrouted response, markdown
        // wrapping, truncated output) — log enough context to diagnose it.
        if (tasks.Count == 0)
        {
            _log.LogWarning(
                "Architect returned zero tasks. Exit={ExitCode}, Output head='{Head}', Error head='{ErrHead}'",
                result.ExitCode,
                Truncate(parsedResp.Output ?? result.Output ?? "", 400),
                Truncate(result.Error ?? "", 200));
        }

        // TaskListJson is a serialized echo of Tasks; cap it so pathological
        // architect outputs (30+ tasks with long descriptions) don't balloon
        // the parent's Temporal history. The structured `Tasks` list is the
        // authoritative payload consumers iterate over.
        return new ArchitectOutput(
            TaskListJson: TruncateForHistory(JsonSerializer.Serialize(tasks), 16 * 1024),
            TaskCount: tasks.Count,
            Tasks: tasks);
    }


    /// <summary>
    /// Day 5 — long-running research activity. Streams CLI output with periodic
    /// heartbeats so Temporal can resume from the last observed line on retry.
    /// Strong model by caller's <paramref name="input.ModelPower"/> (typical: 2).
    /// Splits the final response into rewritten / analysis / context / rationale
    /// sections. temporal.md §I.1.
    /// </summary>
    [Activity]
    public async Task<ResearchPromptOutput> ResearchPromptAsync(ResearchPromptInput input)
    {
        if (string.IsNullOrWhiteSpace(input.ContainerId))
            throw new ApplicationFailureException(
                "ResearchPrompt requires a ContainerId; AI agents always run inside a worker container.",
                errorType: "ConfigError", nonRetryable: true);

        var ctx = ActivityExecutionContext.Current;
        var ct = ctx.CancellationToken;

        // Resume marker: on retry, skip output we already streamed.
        var resumeOffset = 0;
        if (ctx.Info.HeartbeatDetails.Count > 0)
        {
            try
            {
                resumeOffset = await ctx.Info.HeartbeatDetailAtAsync<int>(0);
            }
            catch
            {
                resumeOffset = 0;
            }
        }

        var assistantName = AiAssistantResolver.NormalizeAssistant(input.AiAssistant, _config.DefaultAgent);
        var runner = _factory.Create(assistantName);

        var researchPrompt = $"""
            Research the codebase in the current working directory. Identify files,
            patterns, and conventions relevant to this task. Then return a grounded
            rewrite of the task and a summary of your findings.

            Task: {input.Prompt}

            Structure your response as four markdown H2 sections exactly in this order:
            ## Rewritten Task
            (a detailed rewrite of the task grounded in the codebase)
            ## Codebase Analysis
            (key files, patterns, conventions relevant to the task)
            ## Research Context
            (external docs or links, if relevant)
            ## Rationale
            (why the rewrite is complete and grounded)
            """;

        var modelPower = input.ModelPower > 0 ? input.ModelPower : 2;

        var request = new AgentRequest
        {
            Prompt = researchPrompt,
            Model = AiAssistantResolver.ResolveModelForPower(runner, _config, modelPower),
            WorkDir = _config.ContainerWorkDir ?? "/workspace",
            SessionId = null /* fresh Claude session — continuity only in RunCliAgentAsync */
        };

        var plan = runner.BuildExecutionPlan(request);

        foreach (var setup in plan.SetupRequests ?? [])
            await _docker.ExecAsync(input.ContainerId, setup, ct);

        var captured = new StringBuilder();
        var lineCount = 0;
        var timeout = TimeSpan.FromMinutes(30);

        void OnOutput(string chunk)
        {
            foreach (var line in chunk.Split('\n'))
            {
                if (string.IsNullOrEmpty(line)) continue;
                lineCount++;
                if (lineCount <= resumeOffset) continue;

                captured.AppendLine(line);

                if (input.SessionId is not null)
                {
                    try
                    {
                        _sink.EmitChunkAsync(input.SessionId, line, ct)
                            .GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        _log.LogDebug(ex, "Sink emit failed for {SessionId}", input.SessionId);
                    }
                }

                if (lineCount % 20 == 0)
                    ctx.Heartbeat(lineCount);
            }
        }

        // Background keep-alive heartbeat — see RunCliAgentAsync for rationale.
        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var heartbeatTask = Task.Run(async () =>
        {
            try
            {
                while (!heartbeatCts.Token.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), heartbeatCts.Token);
                    ctx.Heartbeat(lineCount);
                }
            }
            catch (OperationCanceledException) { /* expected on teardown */ }
        }, heartbeatCts.Token);

        ExecResult result;
        try
        {
            result = await _docker.ExecStreamingAsync(
                input.ContainerId,
                plan.MainRequest,
                OnOutput,
                timeout,
                ct);
        }
        catch (OperationCanceledException)
        {
            _log.LogInformation("ResearchPrompt cancelled at line {Line}", lineCount);
            throw;
        }
        finally
        {
            heartbeatCts.Cancel();
            try { await heartbeatTask; } catch { /* ignore shutdown noise */ }
        }

        // Prefer the raw ExecResult.Output buffer over the line-split `captured`
        // buffer when parsing. The callback splits chunks on '\n' which is lossy
        // when a Windows PTY injects \r\n inside JSON string literals mid-object
        // (docker exec wraps at ~256 chars). ExecResult.Output retains the full
        // stream verbatim, so ClaudeRunner.ParseResponse's balanced-brace scanner
        // can reconstitute objects even when the wrap tore them apart.
        var raw = !string.IsNullOrEmpty(result.Output)
            ? result.Output
            : captured.ToString();

        if (AuthErrorDetector.ContainsAuthError(raw)
            || AuthErrorDetector.ContainsAuthError(result.Error))
        {
            _log.LogWarning("ResearchPrompt auth error detected; attempting credential recovery");
            var (recovered, authError, credsJson) = await _auth.RecoverAuthAsync(ct);
            if (recovered && !string.IsNullOrEmpty(credsJson))
            {
                await CredentialInjector.InjectAsync(input.ContainerId, credsJson, ct);
                throw new ApplicationFailureException(
                    "Auth recovered; retrying",
                    errorType: "AuthRefreshed",
                    nonRetryable: false);
            }

            // No AuthServiceUrl → heuristic match, not a real expired token.
            if (string.Equals(authError, "AuthServiceUrl not configured", StringComparison.Ordinal))
            {
                _log.LogWarning("ResearchPrompt: auth-like pattern but AuthServiceUrl unconfigured; continuing.");
            }
            else
            {
                throw new ApplicationFailureException(
                    $"Auth recovery failed: {authError ?? "no details"}",
                    errorType: "AuthError",
                    nonRetryable: true);
            }
        }

        string responseText;
        try
        {
            var parsed = runner.ParseResponse(raw);
            responseText = !string.IsNullOrWhiteSpace(parsed.Output) ? parsed.Output! : raw;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "ResearchPrompt ParseResponse threw; using raw output");
            responseText = raw;
        }

        var (rewritten, analysis, context, rationale) = SplitResearchOutput(responseText);
        // Research can emit megabytes of codebase scanning output. The downstream
        // consumers need the high-signal summaries (enhanced prompt, analysis,
        // context); the raw firehose already reached the UI via SignalR.
        const int MaxResearchBytes = 16 * 1024;
        return new ResearchPromptOutput(
            EnhancedPrompt: TruncateForHistory(rewritten, MaxResearchBytes),
            CodebaseAnalysis: TruncateForHistory(analysis, MaxResearchBytes),
            ResearchContext: TruncateForHistory(context, MaxResearchBytes),
            Rationale: TruncateForHistory(rationale, 2 * 1024));
    }

    /// <summary>
    /// Day 5 — convenience wrapper around <see cref="ClassifyAsync"/> that asks a
    /// fixed question to decide whether a task is website / frontend related.
    /// Used to gate the website-audit branch in orchestration workflows. temporal.md §I.1.
    /// </summary>
    [Activity]
    public async Task<WebsiteClassifyOutput> ClassifyWebsiteTaskAsync(WebsiteClassifyInput input)
    {
        // The website-audit branch is ONLY for auditing an EXISTING website
        // (checking usability / accessibility / performance of pages, navigation,
        // forms, checkout, footer). It is NOT for building new web applications,
        // games, SPAs, or frontend code from scratch. Make the classifier
        // question match that narrow scope so prompts like "build a browser
        // game" aren't mis-routed into the audit pipeline.
        var classify = await ClassifyAsync(new ClassifierInput(
            Prompt: input.Prompt,
            ClassificationQuestion:
                "Is the user asking to AUDIT or ANALYZE an EXISTING website "
                + "(pages, navigation, forms, usability, accessibility, "
                + "performance)? Return true ONLY for audit/review/analysis "
                + "tasks against a website that already exists. Return false "
                + "for building, creating, developing, or implementing any "
                + "new web app, game, SPA, frontend, or any new code — even "
                + "when the deliverable runs in a browser.",
            ContainerId: input.ContainerId,
            ModelPower: 3,
            AiAssistant: input.AiAssistant,
            SessionId: input.SessionId));

        return new WebsiteClassifyOutput(
            IsWebsiteTask: classify.Result,
            Confidence: classify.Confidence,
            Rationale: classify.Rationale);
    }

    /// <summary>
    /// Day 5 — grade whether the work done inside the container meets the original
    /// requirement. Returns a gap prompt when coverage is insufficient, so the
    /// orchestrator can feed it into the next iteration. temporal.md §I.1.
    /// </summary>
    [Activity]
    public async Task<CoverageOutput> GradeCoverageAsync(CoverageInput input)
    {
        if (string.IsNullOrWhiteSpace(input.ContainerId))
            throw new ApplicationFailureException(
                "GradeCoverage requires a ContainerId; AI agents always run inside a worker container.",
                errorType: "ConfigError", nonRetryable: true);

        var ct = ActivityExecutionContext.Current.CancellationToken;

        var assistantName = AiAssistantResolver.NormalizeAssistant(input.AiAssistant, _config.DefaultAgent);
        var runner = _factory.Create(assistantName);

        var coveragePrompt = $$"""
            Grade the completed work against this original requirement.
            Requirement: {{input.OriginalPrompt}}
            Iteration: {{input.CurrentIteration}} of {{input.MaxIterations}}

            Return JSON only:
            {
              "allMet": true|false,
              "gapPrompt": "if not all met, a prompt to fix the gap; otherwise empty",
              "report": "structured report"
            }
            """;

        const string schema = """
            {"type":"object","properties":{"allMet":{"type":"boolean"},"gapPrompt":{"type":"string"},"report":{"type":"string"}},"required":["allMet","gapPrompt","report"],"additionalProperties":false}
            """;

        var modelPower = input.ModelPower > 0 ? input.ModelPower : 2;

        var request = new AgentRequest
        {
            Prompt = coveragePrompt,
            Model = AiAssistantResolver.ResolveModelForPower(runner, _config, modelPower),
            OutputSchema = schema,
            WorkDir = NormalizeContainerWorkDir(input.WorkingDirectory),
            SessionId = null /* fresh Claude session — continuity only in RunCliAgentAsync */
        };

        var plan = runner.BuildExecutionPlan(request);

        foreach (var setup in plan.SetupRequests ?? [])
            await _docker.ExecAsync(input.ContainerId, setup, ct);

        ExecResult result;
        try
        {
            result = await _docker.ExecAsync(input.ContainerId, plan.MainRequest, ct);

            if (result.ExitCode != 0
                && AuthErrorDetector.ContainsAuthError((result.Output ?? "") + (result.Error ?? "")))
            {
                _log.LogWarning("GradeCoverage auth error detected; attempting credential recovery");
                var (recovered, authError, credsJson) = await _auth.RecoverAuthAsync(ct);
                if (recovered && !string.IsNullOrEmpty(credsJson))
                {
                    await CredentialInjector.InjectAsync(input.ContainerId, credsJson, ct);
                    result = await _docker.ExecAsync(input.ContainerId, plan.MainRequest, ct);
                }
                else
                {
                    _log.LogWarning("GradeCoverage auth recovery failed: {Error}", authError);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "GradeCoverage threw; returning retry-in-plain-English gap");
            return new CoverageOutput(
                AllMet: false,
                GapPrompt: "Retry in plain English.",
                CoverageReportJson: "{}",
                Iteration: input.CurrentIteration);
        }

        try
        {
            var parsedResp = runner.ParseResponse(result.Output ?? "");
            var body = !string.IsNullOrWhiteSpace(parsedResp.StructuredOutputJson)
                ? parsedResp.StructuredOutputJson!
                : parsedResp.Output ?? result.Output ?? "{}";
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            // GapPrompt is consumed by the next RunCliAgent call so keep a
            // useful length; CoverageReportJson is a diagnostic echo of the
            // body so cap it to 8 KB for history.
            var gapPrompt = root.TryGetProperty("gapPrompt", out var g) ? g.GetString() ?? "" : "";
            return new CoverageOutput(
                AllMet: root.TryGetProperty("allMet", out var a) && a.GetBoolean(),
                GapPrompt: TruncateForHistory(gapPrompt, 4 * 1024),
                CoverageReportJson: TruncateForHistory(body, 8 * 1024),
                Iteration: input.CurrentIteration);
        }
        catch
        {
            return new CoverageOutput(
                AllMet: false,
                GapPrompt: "Retry in plain English.",
                CoverageReportJson: "{}",
                Iteration: input.CurrentIteration);
        }
    }

    // ── SmartImprove activities ─────────────────────────────────────────
    //
    // GenerateRubricAsync, PlanVerificationHarnessAsync, PickNextImprovementAsync,
    // UpdateBacklogAsync, ClassifyFailuresAsync — these wrap the model with
    // SmartImprove-specific prompts and structured-output schemas. The shape
    // mirrors GradeCoverageAsync (single CLI call, JSON in/out, auth retry,
    // graceful fallback on parse failure). See newplan.md §2.2 + §3.

    /// <summary>
    /// Phase 0 preprocess. Read PROJECT_PROFILE.md (gathered earlier by
    /// ContextGathererWorkflow) and the original user prompt; produce a
    /// project-type-specific completion rubric. The rubric is the rest of
    /// the loop's "definition of done" — every termination decision flows
    /// from this.
    /// </summary>
    [Activity]
    public async Task<GenerateRubricOutput> GenerateRubricAsync(GenerateRubricInput input)
    {
        if (string.IsNullOrWhiteSpace(input.ContainerId))
            throw new ApplicationFailureException(
                "GenerateRubric requires a ContainerId.",
                errorType: "ConfigError", nonRetryable: true);

        var ct = ActivityExecutionContext.Current.CancellationToken;
        var assistantName = AiAssistantResolver.NormalizeAssistant(
            input.AiAssistant, _config.DefaultAgent);
        var runner = _factory.Create(assistantName);

        var rubricPrompt = $$"""
            You are designing a definition-of-done rubric for an autonomous
            improvement loop. Read the PROJECT PROFILE and ORIGINAL PROMPT
            below, then produce a rubric tailored to this specific project's
            type.

            ## PROJECT PROFILE
            {{input.ProjectProfile}}

            ## ORIGINAL PROMPT
            {{input.OriginalPrompt}}

            ## INSTRUCTIONS
            1. Detect the project type ("game" | "web" | "api" | "cli" |
               "library" | "desktop" | "unknown"). If genuinely ambiguous,
               return "unknown" — do NOT guess.
            2. Produce 6–20 rubric items. Each must be EXTERNALLY verifiable
               (a bash command, an HTTP probe, a Playwright spec) — not the
               model's own self-report.
            3. **Prefer BEHAVIORAL/FUNCTIONAL checks over SOURCE-PATTERN checks.**
               This is the most important rule. Source-pattern checks (grep
               for specific strings in .cs / .ts / .py files) are fragile —
               an improved implementation may keep behavior identical while
               changing the literal source. Examples:
                 BAD  : grep -qE 'return\s+a\s*\+\s*b' Calc.cs
                 GOOD : dotnet test --filter Add_ReturnsCorrectSum
                 BAD  : grep -q 'app.UseHttpsRedirection' Program.cs
                 GOOD : curl -sI http://localhost:5000/foo | head -1 | grep '301\|HTTPS'
                 BAD  : grep -q '\\[Fact\\]' tests/X.cs
                 GOOD : dotnet test (counts the [Fact]s by running them)
               Use grep ONLY for things that genuinely cannot be tested
               functionally (e.g. "the README has a CONTRIBUTING section").
            4. Assign priorities:
                 P0 = critical / broken / blocker
                 P1 = functional gap or missing feature
                 P2 = polish / UX
                 P3 = nitpick
            5. For each item, choose a verification command pattern:
                 "exit-zero"          — a bash command that should exit 0
                 "regex:<pattern>"    — bash output must match a regex
                 "json-path:<jq>"     — parse JSON output via jq
                 "none"               — manual / TODO
               Examples:
                 dotnet build → exit-zero
                 npm test     → exit-zero
                 curl -s http://localhost:5000/api/foo | jq -e '.ok' → json-path:.ok
                 playwright test e2e/login.spec.ts → exit-zero
            6. **Avoid 'belt-and-suspenders' rubric items.** If you have a
               P0 'tests pass' item AND a P0 'no a-b in source' grep, the
               second is redundant — `tests pass` already proves the
               behavior. Drop the grep, keep the test.
            7. Mark every rubric item IsTrusted=true UNLESS the item refers
               to a test you would have to invent. Trust = pre-existing or
               human-curated.

            Respond with JSON ONLY matching the schema. NO surrounding text.
            """;

        const string schema = """
            {
              "type": "object",
              "properties": {
                "projectType": { "type": "string" },
                "rationale":   { "type": "string" },
                "items": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "properties": {
                      "id":                   { "type": "string" },
                      "description":          { "type": "string" },
                      "priority":             { "type": "string", "enum": ["P0","P1","P2","P3"] },
                      "verificationCommand":  { "type": "string" },
                      "passCriteria":         { "type": "string" },
                      "isTrusted":            { "type": "boolean" }
                    },
                    "required": ["id","description","priority","verificationCommand","passCriteria"],
                    "additionalProperties": false
                  }
                }
              },
              "required": ["projectType","rationale","items"],
              "additionalProperties": false
            }
            """;

        var modelPower = input.ModelPower > 0 ? input.ModelPower : 2;

        var request = new AgentRequest
        {
            Prompt = rubricPrompt,
            Model = AiAssistantResolver.ResolveModelForPower(runner, _config, modelPower),
            OutputSchema = schema,
            WorkDir = NormalizeContainerWorkDir(input.WorkspacePath),
            SessionId = null,
        };

        var (rawJson, costUsd) = await RunAiCallWithAuthRetryAsync(
            input.ContainerId, runner, request, ct, "GenerateRubric");

        // Defensive parse — the model is told to emit JSON only, but
        // structured-output enforcement is best-effort. Fall back to a
        // stub rubric so the caller can decide whether to retry.
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;
            var projectType = root.TryGetProperty("projectType", out var pt)
                ? pt.GetString() ?? "unknown" : "unknown";
            var rationale = root.TryGetProperty("rationale", out var r)
                ? r.GetString() ?? "" : "";
            var items = root.TryGetProperty("items", out var i) ? i.GetArrayLength() : 0;

            // Persist for downstream activities (PlanHarness, PickNext, UpdateBacklog).
            await PersistArtifactAsync(
                input.ContainerId,
                input.WorkspacePath,
                ".smartimprove/rubric.json",
                rawJson,
                ct);

            return new GenerateRubricOutput(
                ProjectType: projectType,
                Rationale: rationale,
                RubricJson: TruncateForHistory(rawJson, 32 * 1024),
                RubricItemCount: items,
                CostUsd: costUsd);
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "GenerateRubric returned non-JSON; emitting stub rubric");
            var stub = """
                { "projectType": "unknown", "rationale": "rubric generation failed; stub returned",
                  "items": [] }
                """;
            return new GenerateRubricOutput(
                ProjectType: "unknown",
                Rationale: "rubric generation failed: " + Truncate(ex.Message),
                RubricJson: stub,
                RubricItemCount: 0,
                CostUsd: costUsd);
        }
    }

    /// <summary>
    /// Phase 0 preprocess. Translate the rubric into a runnable bash harness
    /// script. Persists harness.sh to <c>/workspace/.smartimprove/harness.sh</c>
    /// and returns a per-rubric-item command lookup so the verifier can map
    /// failures back to rubric ids.
    /// </summary>
    [Activity]
    public async Task<PlanVerificationHarnessOutput> PlanVerificationHarnessAsync(
        PlanVerificationHarnessInput input)
    {
        if (string.IsNullOrWhiteSpace(input.ContainerId))
            throw new ApplicationFailureException(
                "PlanVerificationHarness requires a ContainerId.",
                errorType: "ConfigError", nonRetryable: true);

        var ct = ActivityExecutionContext.Current.CancellationToken;
        var assistantName = AiAssistantResolver.NormalizeAssistant(
            input.AiAssistant, _config.DefaultAgent);
        var runner = _factory.Create(assistantName);

        var harnessPrompt = $$"""
            You are translating a definition-of-done rubric into a
            self-contained bash verification script that can be re-run any
            time inside the workspace container. The script must be
            idempotent and produce machine-readable per-item results.

            ## PROJECT TYPE
            {{input.ProjectType}}

            ## RUBRIC (JSON)
            {{input.RubricJson}}

            ## INSTRUCTIONS
            1. Emit a single bash script. First line must be `#!/usr/bin/env bash`
               followed by `set -uo pipefail`.
            2. For each rubric item, run its verification command, capture
               stdout/stderr, and emit ONE JSON line on stdout in this exact
               shape (no surrounding text):
                 {"id":"<rubric.id>","status":"pass|fail","exitCode":<n>,"evidence":"<≤200 chars>"}
            3. The script must NOT terminate early on the first failure —
               every item must be reported.
            4. For "playwright:" prefixed verification commands, treat the
               remainder as a spec path under /workspace/.smartimprove/playwright/
               and shell out to `npx playwright test <spec> --reporter=line`.
            5. For "http:" prefixed commands of form "http:<url>:<expected_status>",
               run `curl -s -o /dev/null -w '%{http_code}' <url>` and compare.
            6. Also produce a CommandsByRubricId lookup so the workflow can map
               failures back to rubric ids without re-parsing the script.

            Respond with JSON ONLY matching the schema below.
            """;

        const string schema = """
            {
              "type": "object",
              "properties": {
                "harnessScript":         { "type": "string" },
                "commandsByRubricId":    {
                   "type": "object",
                   "additionalProperties": { "type": "string" }
                }
              },
              "required": ["harnessScript","commandsByRubricId"],
              "additionalProperties": false
            }
            """;

        var modelPower = input.ModelPower > 0 ? input.ModelPower : 2;

        var request = new AgentRequest
        {
            Prompt = harnessPrompt,
            Model = AiAssistantResolver.ResolveModelForPower(runner, _config, modelPower),
            OutputSchema = schema,
            WorkDir = NormalizeContainerWorkDir(input.WorkspacePath),
            SessionId = null,
        };

        var (rawJson, costUsd) = await RunAiCallWithAuthRetryAsync(
            input.ContainerId, runner, request, ct, "PlanVerificationHarness");

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;
            var script = root.TryGetProperty("harnessScript", out var s)
                ? s.GetString() ?? "" : "";
            var commands = new Dictionary<string, string>(StringComparer.Ordinal);
            if (root.TryGetProperty("commandsByRubricId", out var cmds)
                && cmds.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in cmds.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.String)
                        commands[prop.Name] = prop.Value.GetString() ?? "";
                }
            }

            const string harnessPath = ".smartimprove/harness.sh";
            await PersistArtifactAsync(input.ContainerId, input.WorkspacePath, harnessPath, script, ct);
            await ChmodExecutableAsync(input.ContainerId, input.WorkspacePath, harnessPath, ct);

            return new PlanVerificationHarnessOutput(
                HarnessScriptPath: harnessPath,
                CommandsByRubricId: commands,
                CostUsd: costUsd);
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "PlanVerificationHarness returned non-JSON; harness not persisted");
            return new PlanVerificationHarnessOutput(
                HarnessScriptPath: "",
                CommandsByRubricId: new Dictionary<string, string>(),
                CostUsd: costUsd);
        }
    }

    /// <summary>
    /// LLM-as-judge that buckets verifier failures into
    /// <c>{real | structural | environmental}</c> using the failure evidence
    /// + recent diff context. Per Hamel Husain's "LLM-as-Judge" guidance,
    /// LLM-as-judge is appropriate for the SUBJECTIVE classification step
    /// (why did this fail) but not for the OBJECTIVE termination decision
    /// (which is left to the deterministic harness). See newplan.md §3.3.
    /// </summary>
    /// <remarks>
    /// Routing of the result classes:
    /// <list type="bullet">
    /// <item><c>real</c> — added to backlog as the same priority the rubric assigned</item>
    /// <item><c>structural</c> — selector/locator drift; spawn a separate
    /// "selector heal" sub-task; does NOT drive the next fix burst</item>
    /// <item><c>environmental</c> — flake/port-conflict/network; retry once,
    /// quarantine if it persists; does NOT drive the next fix burst</item>
    /// </list>
    /// Mis-classification risk (recorded in newplan.md §10) is contained by
    /// always logging full evidence so an operator can audit decisions in
    /// Studio.
    /// </remarks>
    [Activity]
    public async Task<ClassifyFailuresOutput> ClassifyFailuresAsync(ClassifyFailuresInput input)
    {
        if (string.IsNullOrWhiteSpace(input.ContainerId))
            throw new ApplicationFailureException(
                "ClassifyFailures requires a ContainerId.",
                errorType: "ConfigError", nonRetryable: true);

        var ct = ActivityExecutionContext.Current.CancellationToken;
        var assistantName = AiAssistantResolver.NormalizeAssistant(
            input.AiAssistant, _config.DefaultAgent);
        var runner = _factory.Create(assistantName);

        // Cap incoming text to keep the prompt under 24 KB which fits in a
        // single CLI argv on every supported runner without resorting to stdin.
        var truncatedHarness = TruncateForHistory(input.HarnessOutput ?? "", 12 * 1024);
        var truncatedFailures = TruncateForHistory(input.FailuresJson ?? "[]", 12 * 1024);

        var classifyPrompt = $$"""
            You are triaging verifier failures from an autonomous code-improvement
            loop. For EACH failure, decide whether it represents a REAL bug in
            the model's code, a STRUCTURAL drift (locator/selector/element
            renamed but functionality intact), or an ENVIRONMENTAL flake
            (port-in-use, network blip, race, missing CLI tool).

            ## RUBRIC FAILURES (JSON array)
            {{truncatedFailures}}

            ## RAW HARNESS OUTPUT (truncated)
            {{truncatedHarness}}

            ## CLASSIFICATION RULES
            - "real"          — the underlying functionality is wrong/missing.
                                e.g. compile error, wrong assertion result,
                                wrong HTTP response shape, broken UI flow.
            - "structural"    — the test cannot find an element / endpoint
                                because of a NAME change, but the underlying
                                feature still works. Indicates the test needs
                                to be updated, not the source.
            - "environmental" — the test can't run at all due to env state.
                                Examples: "EADDRINUSE", "Cannot find module
                                @playwright/test", "ECONNREFUSED localhost:5000",
                                missing Docker socket. NOT a code defect.

            Return JSON ONLY: an array of {id, classification, reason} entries
            in the SAME order as the input failures. The "reason" field is a
            ≤120 char one-liner explaining the classification.
            """;

        const string schema = """
            {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "id":             { "type": "string" },
                  "classification": { "type": "string", "enum": ["real","structural","environmental"] },
                  "reason":         { "type": "string" }
                },
                "required": ["id","classification","reason"],
                "additionalProperties": false
              }
            }
            """;

        var modelPower = input.ModelPower > 0 ? input.ModelPower : 3;

        var request = new AgentRequest
        {
            Prompt = classifyPrompt,
            Model = AiAssistantResolver.ResolveModelForPower(runner, _config, modelPower),
            OutputSchema = schema,
            WorkDir = NormalizeContainerWorkDir(input.WorkspacePath),
            SessionId = null,
        };

        var (rawJson, costUsd) = await RunAiCallWithAuthRetryAsync(
            input.ContainerId, runner, request, ct, "ClassifyFailures");

        // Default: keep the original failures unchanged if classification
        // failed to parse — better to let the workflow act on un-downgraded
        // "real" failures (safer) than to silently drop them.
        try
        {
            using var inputDoc = JsonDocument.Parse(input.FailuresJson ?? "[]");
            using var classDoc = JsonDocument.Parse(rawJson);

            // Build a lookup of id → classification. The model is told to
            // preserve order, but we don't trust it for long lists.
            var classByid = new Dictionary<string, (string Cls, string Reason)>(StringComparer.Ordinal);
            if (classDoc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in classDoc.RootElement.EnumerateArray())
                {
                    var id = entry.TryGetProperty("id", out var i) ? i.GetString() ?? "" : "";
                    var cls = entry.TryGetProperty("classification", out var c) ? c.GetString() ?? "real" : "real";
                    var reason = entry.TryGetProperty("reason", out var r) ? r.GetString() ?? "" : "";
                    if (!string.IsNullOrEmpty(id))
                        classByid[id] = (Normalize(cls), reason);
                }
            }

            // Materialize merged result, preserving original failure shape.
            var merged = new List<object>();
            int real = 0, structural = 0, environmental = 0;
            if (inputDoc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var f in inputDoc.RootElement.EnumerateArray())
                {
                    var id = f.TryGetProperty("rubricItemId", out var iid)
                        ? iid.GetString() ?? "" :
                        f.TryGetProperty("id", out var ii) ? ii.GetString() ?? "" : "";
                    var prio = f.TryGetProperty("priority", out var pp) ? pp.GetString() ?? "P1" : "P1";
                    var ev = f.TryGetProperty("evidence", out var ee) ? ee.GetString() ?? "" : "";

                    var cls = classByid.TryGetValue(id, out var hit) ? hit.Cls : "real";
                    switch (cls)
                    {
                        case "structural":    structural++; break;
                        case "environmental": environmental++; break;
                        default:              real++; cls = "real"; break;
                    }

                    merged.Add(new
                    {
                        rubricItemId = id,
                        priority = prio,
                        classification = cls,
                        evidence = ev,
                    });
                }
            }

            var classifiedJson = JsonSerializer.Serialize(merged);

            return new ClassifyFailuresOutput(
                ClassifiedFailuresJson: TruncateForHistory(classifiedJson, 16 * 1024),
                RealCount: real,
                StructuralCount: structural,
                EnvironmentalCount: environmental,
                CostUsd: costUsd);
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "ClassifyFailures parse failed; falling back to all-real");
            return new ClassifyFailuresOutput(
                ClassifiedFailuresJson: input.FailuresJson ?? "[]",
                RealCount: 0,
                StructuralCount: 0,
                EnvironmentalCount: 0,
                CostUsd: costUsd);
        }
    }

    /// <summary>Normalize the model's classification string to the canonical lowercase form.</summary>
    private static string Normalize(string cls)
    {
        var c = (cls ?? "").Trim().ToLowerInvariant();
        return c switch
        {
            "real" or "structural" or "environmental" => c,
            "bug" or "code" or "defect" => "real",
            "selector" or "locator" or "drift" => "structural",
            "flake" or "flaky" or "env" or "infrastructure" => "environmental",
            _ => "real",
        };
    }

    // ── Shared SmartImprove helpers ─────────────────────────────────────

    /// <summary>
    /// Run a single CLI agent call, recover from auth errors once, return
    /// the parsed body + cost. Centralizes the boilerplate that would
    /// otherwise repeat across every SmartImprove activity.
    /// </summary>
    private async Task<(string Body, decimal CostUsd)> RunAiCallWithAuthRetryAsync(
        string containerId, ICliAgentRunner runner, AgentRequest request,
        CancellationToken ct, string activityName)
    {
        var plan = runner.BuildExecutionPlan(request);
        foreach (var setup in plan.SetupRequests ?? [])
            await _docker.ExecAsync(containerId, setup, ct);

        var result = await _docker.ExecAsync(containerId, plan.MainRequest, ct);

        if (result.ExitCode != 0
            && AuthErrorDetector.ContainsAuthError((result.Output ?? "") + (result.Error ?? "")))
        {
            _log.LogWarning("{Activity} auth error detected; attempting credential recovery", activityName);
            var (recovered, authError, credsJson) = await _auth.RecoverAuthAsync(ct);
            if (recovered && !string.IsNullOrEmpty(credsJson))
            {
                await CredentialInjector.InjectAsync(containerId, credsJson, ct);
                result = await _docker.ExecAsync(containerId, plan.MainRequest, ct);
            }
            else
            {
                _log.LogWarning("{Activity} auth recovery failed: {Error}", activityName, authError);
            }
        }

        var parsed = runner.ParseResponse(result.Output ?? "");
        var body = !string.IsNullOrWhiteSpace(parsed.StructuredOutputJson)
            ? parsed.StructuredOutputJson!
            : parsed.Output ?? result.Output ?? "{}";

        // Cost is parser-reported when the runner extracts it; some runners
        // pass it through StructuredOutputJson tail. For now use 0 — cost
        // accounting upgrade is tracked separately.
        return (body, parsed.CostUsd);
    }

    /// <summary>
    /// Write a text payload to a workspace-relative path inside the
    /// container. Creates parent directories. Used to persist rubric.json,
    /// harness.sh, IMPROVEMENTS.md across iterations.
    /// </summary>
    private async Task PersistArtifactAsync(
        string containerId, string workspacePath, string relPath,
        string content, CancellationToken ct)
    {
        var workspace = NormalizeContainerWorkDir(workspacePath);
        var fullPath = $"{workspace.TrimEnd('/')}/{relPath.TrimStart('/')}";
        var parentDir = fullPath[..fullPath.LastIndexOf('/')];

        // mkdir -p then write payload via stdin to avoid argv length limits.
        var mkdirReq = new ContainerExecRequest(
            FileName: "bash",
            Arguments: new[] { "-lc", $"mkdir -p {ShellEscape(parentDir)}" },
            WorkingDirectory: workspace);
        await _docker.ExecAsync(containerId, mkdirReq, ct);

        var writeReq = new ContainerExecRequest(
            FileName: "bash",
            Arguments: new[] { "-lc", $"cat > {ShellEscape(fullPath)}" },
            WorkingDirectory: workspace,
            StdinInput: content);
        await _docker.ExecAsync(containerId, writeReq, ct);
    }

    private async Task ChmodExecutableAsync(
        string containerId, string workspacePath, string relPath, CancellationToken ct)
    {
        var workspace = NormalizeContainerWorkDir(workspacePath);
        var fullPath = $"{workspace.TrimEnd('/')}/{relPath.TrimStart('/')}";
        var req = new ContainerExecRequest(
            FileName: "chmod",
            Arguments: new[] { "+x", fullPath },
            WorkingDirectory: workspace);
        await _docker.ExecAsync(containerId, req, ct);
    }

    private static string ShellEscape(string s)
    {
        // Single-quote-safe POSIX shell quoting.
        return "'" + s.Replace("'", "'\\''") + "'";
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Normalizes a <c>WorkingDirectory</c> input to a container-side Linux
    /// absolute path. Workflows historically pass <see cref="SimpleAgentInput.WorkspacePath"/>
    /// (a HOST path like <c>C:/tmp/foo</c> on Windows) as the activity's
    /// <c>WorkingDirectory</c>, but Docker <c>exec -w</c> interprets that path
    /// inside the container, producing "OCI runtime exec failed: Cwd must be
    /// an absolute path". Any value that isn't a Linux absolute path (doesn't
    /// start with <c>/</c>) or that contains a Windows drive prefix is replaced
    /// with the container's configured workspace mount point.
    /// </summary>
    internal string NormalizeContainerWorkDir(string? candidate)
    {
        var containerDefault = _config.ContainerWorkDir ?? "/workspace";
        if (string.IsNullOrWhiteSpace(candidate))
            return containerDefault;

        // Linux absolute path — accept as-is.
        if (candidate.StartsWith('/'))
            return candidate;

        // Anything else (Windows drive letter, relative path, UNC path) is not
        // a valid container Cwd — coerce to the mount point.
        return containerDefault;
    }

    private static string BuildTriagePrompt(string userPrompt, string? classificationInstructions)
    {
        var instructionBlock = string.IsNullOrWhiteSpace(classificationInstructions)
            ? "Analyze this coding task and respond with JSON only:"
            : $"{classificationInstructions!.Trim()}\nRespond with JSON only:";

        return
        $$"""
        {{instructionBlock}}
        {
          "complexity": <1-10>,
          "category": "<code_gen|bug_fix|refactor|architecture|testing|docs>",
          "needs_decomposition": <true|false>,
          "recommended_model_power": <1|2>
        }

        Model power guide:
        1 = strongest / deepest reasoning (opus-class)
        2 = balanced default (sonnet-class)
        Always use 1 for complex tasks and 2 for simple tasks. Never recommend 3.

        Task: {{userPrompt}}
        """;
    }

    private static TriageResult ParseTriageJson(string output)
    {
        try
        {
            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;
            return new TriageResult(
                Complexity: root.TryGetProperty("complexity", out var c) ? c.GetInt32() : 5,
                Category: root.TryGetProperty("category", out var cat) ? cat.GetString() ?? "code_gen" : "code_gen",
                RecommendedModelPower: root.TryGetProperty("recommended_model_power", out var m) ? m.GetInt32() : 2,
                NeedsDecomposition: root.TryGetProperty("needs_decomposition", out var nd) && nd.GetBoolean());
        }
        catch
        {
            return new TriageResult(5, "code_gen", 2, false);
        }
    }

    private static TriageResult FallbackTriageResult(string prompt) =>
        new(Complexity: 5, Category: "code_gen", RecommendedModelPower: 2, NeedsDecomposition: false);

    private static string Truncate(string? value, int maxLength = 2000)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private string? ResolveModel(RunCliAgentInput input, ICliAgentRunner runner)
    {
        if (!string.IsNullOrWhiteSpace(input.Model)
            && !string.Equals(input.Model, "auto", StringComparison.OrdinalIgnoreCase))
            return input.Model;
        if (input.ModelPower > 0)
            return AiAssistantResolver.ResolveModelForPower(runner, _config, input.ModelPower);
        return null;  // runner picks default
    }

    private static List<TaskPlanEntry> ParseTasks(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return new List<TaskPlanEntry>();

        // Try a series of extraction strategies in order, each passing its
        // candidate text through a single safe JsonDocument.Parse path. This
        // covers the three ways Claude actually ships structured JSON:
        //   1. pure JSON body (best case — architect prompt demands it)
        //   2. ```json … ``` or ``` … ``` fenced block embedded in prose
        //   3. raw {…} or […] object sitting inside surrounding narrative
        foreach (var candidate in EnumerateJsonCandidates(output))
        {
            var parsed = TryParseTaskArray(candidate);
            if (parsed is not null && parsed.Count > 0)
                return parsed;
        }
        return new List<TaskPlanEntry>();
    }

    private static IEnumerable<string> EnumerateJsonCandidates(string output)
    {
        // Strategy 1: the whole output, trimmed.
        var trimmed = output.Trim();
        if (trimmed.Length > 0) yield return trimmed;

        // Strategy 2: content inside a ```json ... ``` fence (or bare ``` ... ```
        // fence whose body starts with { or [). We do this with span indexing
        // rather than regex so the parser stays allocation-light and safe.
        var searchStart = 0;
        while (searchStart < output.Length)
        {
            var fenceStart = output.IndexOf("```", searchStart, StringComparison.Ordinal);
            if (fenceStart < 0) break;

            // Skip past the language hint (if any) up to the next newline.
            var bodyStart = output.IndexOf('\n', fenceStart + 3);
            if (bodyStart < 0) break;
            bodyStart++;

            var fenceEnd = output.IndexOf("```", bodyStart, StringComparison.Ordinal);
            if (fenceEnd < 0) break;

            var inner = output[bodyStart..fenceEnd].Trim();
            if (inner.Length > 0 && (inner[0] == '{' || inner[0] == '['))
                yield return inner;

            searchStart = fenceEnd + 3;
        }

        // Strategy 3: the first balanced {...} block in the raw output.
        var objectSpan = ExtractFirstBalancedBlock(output, '{', '}');
        if (objectSpan is not null) yield return objectSpan;

        // Strategy 4: the first balanced [...] block in the raw output.
        var arraySpan = ExtractFirstBalancedBlock(output, '[', ']');
        if (arraySpan is not null) yield return arraySpan;
    }

    // Simple brace scanner — safe (bounded string scan, no regex, no string
    // concat). Returns the substring for the first balanced block at the top
    // level, honoring string literals so braces inside "..." don't confuse
    // the count.
    private static string? ExtractFirstBalancedBlock(string s, char open, char close)
    {
        var start = s.IndexOf(open);
        if (start < 0) return null;
        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var i = start; i < s.Length; i++)
        {
            var ch = s[i];
            if (inString)
            {
                if (escaped) { escaped = false; continue; }
                if (ch == '\\') { escaped = true; continue; }
                if (ch == '"') inString = false;
                continue;
            }
            if (ch == '"') { inString = true; continue; }
            if (ch == open) depth++;
            else if (ch == close)
            {
                depth--;
                if (depth == 0) return s.Substring(start, i - start + 1);
            }
        }
        return null;
    }

    private static List<TaskPlanEntry>? TryParseTaskArray(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Support both { "tasks": [...] } and a bare array at the root.
            JsonElement taskArray;
            if (root.ValueKind == JsonValueKind.Array)
            {
                taskArray = root;
            }
            else if (root.ValueKind == JsonValueKind.Object
                     && root.TryGetProperty("tasks", out var tasksProp)
                     && tasksProp.ValueKind == JsonValueKind.Array)
            {
                taskArray = tasksProp;
            }
            else
            {
                return null;
            }

            var list = new List<TaskPlanEntry>();
            foreach (var t in taskArray.EnumerateArray())
            {
                if (t.ValueKind != JsonValueKind.Object) continue;
                list.Add(new TaskPlanEntry(
                    Id: t.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? Guid.NewGuid().ToString() : Guid.NewGuid().ToString(),
                    Description: t.TryGetProperty("description", out var descEl) ? descEl.GetString() ?? "" : "",
                    DependsOn: t.TryGetProperty("dependsOn", out var d) && d.ValueKind == JsonValueKind.Array
                        ? d.EnumerateArray().Select(x => x.GetString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToArray()
                        : Array.Empty<string>(),
                    FilesTouched: t.TryGetProperty("filesTouched", out var f) && f.ValueKind == JsonValueKind.Array
                        ? f.EnumerateArray().Select(x => x.GetString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToArray()
                        : Array.Empty<string>()));
            }
            return list;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static (string rewritten, string analysis, string context, string rationale) SplitResearchOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return ("", "", "", "");

        // Split on markdown H2 sections. The first part before any ## is dropped
        // (that's typically preamble); each remaining section is the header line
        // followed by its body.
        var sections = output.Split("\n## ", StringSplitOptions.None);

        string FindSection(string keyword)
        {
            foreach (var section in sections)
            {
                var trimmed = section.TrimStart();
                if (trimmed.StartsWith(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    // Strip the header line and return the body.
                    var newlineIdx = trimmed.IndexOf('\n');
                    return newlineIdx < 0 ? "" : trimmed[(newlineIdx + 1)..].Trim();
                }
            }
            return "";
        }

        var rewritten = FindSection("Rewritten");
        var analysis = FindSection("Codebase");
        var context = FindSection("Research");
        var rationale = FindSection("Rationale");

        // If no sections matched at all, fall back to the entire output as both
        // the rewritten prompt and the codebase analysis so downstream callers
        // still get useful content instead of empty strings.
        if (string.IsNullOrEmpty(rewritten)
            && string.IsNullOrEmpty(analysis)
            && string.IsNullOrEmpty(context)
            && string.IsNullOrEmpty(rationale))
        {
            var fallback = output.Trim();
            rewritten = fallback;
            analysis = fallback;
        }

        return (rewritten, analysis, context, rationale);
    }
}
