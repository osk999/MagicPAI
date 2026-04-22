using System.Text;
using System.Text.Json;
using MagicPAI.Core.Models;

namespace MagicPAI.Core.Services;

public class ClaudeRunner : ICliAgentRunner
{
    private const string NonInteractiveSystemPrompt =
        "IMPORTANT: You are running in fully non-interactive mode. " +
        "stdin is closed after the initial prompt. " +
        "Do NOT use plan mode. Do NOT call ExitPlanMode. " +
        "Execute all tasks directly and immediately without requesting user approval. " +
        "Never pause and never ask for confirmation - just implement everything now.";

    // Bump these when Anthropic ships a new model version.
    private const string HaikuModelId = "claude-haiku-4-5";
    private const string SonnetModelId = "claude-sonnet-4-6";
    private const string OpusModelId = "claude-opus-4-7";

    public string AgentName => "claude";
    public string DefaultModel => "sonnet";
    public string[] AvailableModels =>
    [
        "haiku", "sonnet", "opus",
        HaikuModelId, SonnetModelId, OpusModelId
    ];
    public bool SupportsNativeSchema => true; // --json-schema flag

    public string BuildCommand(AgentRequest request)
    {
        var model = ResolveModel(request.Model ?? DefaultModel);
        var isWindows = OperatingSystem.IsWindows();

        // Platform-aware quoting: Windows cmd uses double quotes, bash uses single
        var q = isWindows ? "\"" : "'";
        var prompt = isWindows ? EscapeWindows(request.Prompt ?? "") : Escape(request.Prompt ?? "");

        // cd path: don't quote on Windows (cmd.exe doesn't handle quoted cd with forward slashes)
        var workDir = request.WorkDir ?? "/workspace";
        var cdPath = isWindows ? workDir : $"'{workDir}'";
        var cmd = $"cd {cdPath} && claude" +
                  $" --dangerously-skip-permissions" +
                  $" --setting-sources project,local" +
                  $" --model {model}" +
                  $" --output-format stream-json" +
                  $" --include-partial-messages" +
                  $" --verbose";

        var nonInteractivePrompt = isWindows
            ? EscapeWindows(NonInteractiveSystemPrompt)
            : Escape(NonInteractiveSystemPrompt);
        cmd += $" --append-system-prompt {q}{nonInteractivePrompt}{q}";

        if (!string.IsNullOrWhiteSpace(request.SessionId))
        {
            var sessionId = isWindows ? EscapeWindows(request.SessionId) : Escape(request.SessionId);
            cmd += $" --resume {q}{sessionId}{q}";
        }

        cmd += $" -p {q}{prompt}{q}";

        if (request.MaxBudgetUsd > 0)
            cmd += $" --max-budget-usd {request.MaxBudgetUsd:F2}";

        if (!string.IsNullOrEmpty(request.OutputSchema))
        {
            var schema = isWindows ? EscapeWindows(request.OutputSchema) : Escape(request.OutputSchema);
            cmd += $" --json-schema {q}{schema}{q}";
        }

        return cmd;
    }

    public CliAgentExecutionPlan BuildExecutionPlan(AgentRequest request)
    {
        var model = ResolveModel(request.Model ?? DefaultModel);
        var arguments = new List<string>
        {
            "--dangerously-skip-permissions",
            "--setting-sources",
            "project,local",
            "--model",
            model,
            "--output-format",
            "stream-json",
            "--include-partial-messages",
            "--append-system-prompt",
            NonInteractiveSystemPrompt,
            "--verbose"
        };

        if (request.MaxBudgetUsd > 0)
        {
            arguments.Add("--max-budget-usd");
            arguments.Add($"{request.MaxBudgetUsd:F2}");
        }

        if (!string.IsNullOrWhiteSpace(request.OutputSchema))
        {
            arguments.Add("--json-schema");
            arguments.Add(request.OutputSchema);
        }

        if (!string.IsNullOrWhiteSpace(request.SessionId))
        {
            arguments.Add("--resume");
            arguments.Add(request.SessionId);
        }

        // Pre-flight argv size. Windows CreateProcess caps the combined
        // command line at ~32 KB; the Linux MAX_ARG_STRLEN is higher but still
        // finite. When the prompt would push us past the cap we drop it from
        // argv and pipe it through the Claude CLI's stdin instead (`-p` with
        // no prompt argument reads from stdin).
        var baseArgBytes = 0;
        foreach (var a in arguments) baseArgBytes += System.Text.Encoding.UTF8.GetByteCount(a) + 1;
        var promptBytes = System.Text.Encoding.UTF8.GetByteCount(request.Prompt ?? "");
        // +3 for "-p " and its separator byte
        var argvWithPrompt = baseArgBytes + 3 + promptBytes;

        string? stdinPayload = null;
        if (argvWithPrompt > MaxArgvBytes)
        {
            // Over the cap → stdin mode. `-p` without a value tells the
            // Claude CLI to read the prompt from standard input.
            arguments.Add("-p");
            stdinPayload = request.Prompt ?? "";
        }
        else
        {
            arguments.Add("-p");
            arguments.Add(request.Prompt ?? "");
        }

        return new CliAgentExecutionPlan(
            new ContainerExecRequest(
                FileName: "claude",
                Arguments: arguments,
                WorkingDirectory: request.WorkDir ?? "/workspace",
                StdinInput: stdinPayload));
    }

    // Conservative cross-platform ceiling: Windows CreateProcess caps the
    // combined command line at 32,767 chars (minus ~200 for "docker exec … bash -c"
    // wrapping). 28 KB leaves comfortable headroom.
    private const int MaxArgvBytes = 28 * 1024;

    public CliAgentResponse ParseResponse(string rawOutput)
    {
        // Claude emits one JSON object per line in stream-json mode, but on Windows
        // `docker exec` wraps stdout through a PTY that injects spurious \r\n inside
        // JSON string literals at ~256-char boundaries. A naive Split('\n') yields
        // broken "half lines" that TryParse rejects, so we fall back to a
        // balanced-brace scanner that reconstitutes each top-level JSON object
        // regardless of where newlines landed in the wire stream.
        var objects = SplitBalancedJsonObjects(rawOutput);

        JsonElement? lastResult = null;
        foreach (var obj in objects)
        {
            var parsed = TryParseJson(obj);
            if (parsed is null) continue;
            if (parsed.Value.ValueKind != JsonValueKind.Object) continue;
            if (!parsed.Value.TryGetProperty("type", out var t)) continue;
            if (t.GetString() == "result")
                lastResult = parsed;
        }

        if (lastResult is null)
        {
            // No `type:result` envelope found — return the cleaned buffer so the
            // caller can inspect it, but flag Success=false so downstream does
            // not treat a garbled stream as an authoritative answer.
            var cleaned = CleanPtyArtifacts(rawOutput);
            return new(false, cleaned, 0, [], 0, 0, null);
        }

        var r = lastResult.Value;

        // Extract structured_output if present (from --json-schema)
        var structuredOutput = r.TryGetProperty("structured_output", out var so) && so.ValueKind != JsonValueKind.Null
            ? so.GetRawText()
            : null;
        var output = structuredOutput
            ?? (r.TryGetProperty("result", out var res) ? res.GetString() ?? "" : "");

        return new(
            Success: !r.TryGetProperty("is_error", out var e) || !e.GetBoolean(),
            Output: output,
            CostUsd: ExtractCost(r),
            FilesModified: ExtractFiles(r),
            InputTokens: ExtractTokens(r, "input"),
            OutputTokens: ExtractTokens(r, "output"),
            SessionId: r.TryGetProperty("session_id", out var sid) ? sid.GetString() : null,
            StructuredOutputJson: structuredOutput);
    }

    private static string ResolveModel(string alias) => alias switch
    {
        "haiku" => HaikuModelId,
        "sonnet" => SonnetModelId,
        "opus" => OpusModelId,
        _ => alias
    };

    private static string Escape(string value) => value.Replace("'", "'\\''");
    private static string EscapeWindows(string value) => value.Replace("\"", "\\\"");

    private static JsonElement? TryParseJson(string line)
    {
        try { return JsonDocument.Parse(line).RootElement.Clone(); }
        catch
        {
            // If parse failed, try again after stripping PTY-injected control chars
            // that may have landed inside JSON string literals.
            try { return JsonDocument.Parse(CleanPtyArtifacts(line)).RootElement.Clone(); }
            catch { return null; }
        }
    }

    /// <summary>
    /// Scan a raw Claude stream-json buffer and yield each top-level JSON object
    /// as a substring, regardless of embedded \r\n / \n wrap artifacts. Tracks
    /// brace depth with string-literal / escape awareness so that newlines that
    /// got injected inside a string value are not treated as object boundaries.
    /// </summary>
    internal static IEnumerable<string> SplitBalancedJsonObjects(string raw)
    {
        if (string.IsNullOrEmpty(raw)) yield break;

        var sb = new StringBuilder();
        var depth = 0;
        var inString = false;
        var escape = false;
        var started = false;

        foreach (var ch in raw)
        {
            if (!started)
            {
                // Skip leading whitespace / preamble (e.g. shell banners) until
                // we hit the start of a JSON object.
                if (ch == '{')
                {
                    started = true;
                    sb.Append(ch);
                    depth = 1;
                }
                continue;
            }

            // Inside an object: drop PTY-injected control chars that aren't
            // legal in JSON. Valid JSON allows only \t, \n inside strings — and
            // Claude's stream-json does not use literal newlines inside strings,
            // so any \r or bare \n we encounter while inString is an artifact.
            if (inString && (ch == '\r' || ch == '\n'))
                continue;

            sb.Append(ch);

            if (escape)
            {
                escape = false;
                continue;
            }
            if (ch == '\\')
            {
                escape = true;
                continue;
            }
            if (ch == '"')
            {
                inString = !inString;
                continue;
            }
            if (inString) continue;

            if (ch == '{')
            {
                depth++;
            }
            else if (ch == '}')
            {
                depth--;
                if (depth == 0)
                {
                    yield return sb.ToString();
                    sb.Clear();
                    started = false;
                    inString = false;
                    escape = false;
                }
            }
        }

        // Tail: if a final object is balanced but we never saw more chars, emit it.
        if (sb.Length > 0 && depth == 0)
        {
            var tail = sb.ToString().TrimStart();
            if (tail.StartsWith('{'))
                yield return tail;
        }
    }

    /// <summary>
    /// Drop PTY-injected \r and bare \n bytes that appear inside JSON string
    /// literals. Preserves newlines between top-level objects so downstream
    /// readers can still split by lines if they want to.
    /// </summary>
    internal static string CleanPtyArtifacts(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;

        var sb = new StringBuilder(raw.Length);
        var inString = false;
        var escape = false;

        foreach (var ch in raw)
        {
            if (escape)
            {
                escape = false;
                sb.Append(ch);
                continue;
            }
            if (ch == '\\')
            {
                escape = true;
                sb.Append(ch);
                continue;
            }
            if (ch == '"')
            {
                inString = !inString;
                sb.Append(ch);
                continue;
            }
            if (inString && (ch == '\r' || ch == '\n'))
                continue;
            sb.Append(ch);
        }
        return sb.ToString();
    }

    private static decimal ExtractCost(JsonElement r)
    {
        if (r.TryGetProperty("total_cost_usd", out var c) && c.TryGetDecimal(out var cost))
            return cost;
        if (r.TryGetProperty("cost_usd", out c) && c.TryGetDecimal(out cost))
            return cost;
        return 0;
    }

    private static string[] ExtractFiles(JsonElement r)
    {
        if (!r.TryGetProperty("files_modified", out var files) ||
            files.ValueKind != JsonValueKind.Array)
            return [];

        return files.EnumerateArray()
            .Select(f => f.GetString() ?? "")
            .Where(f => f.Length > 0)
            .ToArray();
    }

    private static int ExtractTokens(JsonElement r, string kind)
    {
        if (r.TryGetProperty("usage", out var usage) &&
            usage.TryGetProperty($"{kind}_tokens", out var tokens) &&
            tokens.TryGetInt32(out var count))
            return count;
        return 0;
    }
}
