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

    public string AgentName => "claude";
    public string DefaultModel => "sonnet";
    public string[] AvailableModels =>
    [
        "haiku", "sonnet", "opus",
        "claude-haiku-4-5", "claude-sonnet-4-6", "claude-opus-4-6"
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

        arguments.Add("-p");
        arguments.Add(request.Prompt ?? "");

        return new CliAgentExecutionPlan(
            new ContainerExecRequest(
                FileName: "claude",
                Arguments: arguments,
                WorkingDirectory: request.WorkDir ?? "/workspace"));
    }

    public CliAgentResponse ParseResponse(string rawOutput)
    {
        var lines = rawOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var lastResult = lines
            .Select(TryParseJson)
            .Where(j => j is not null
                        && j.Value.ValueKind == JsonValueKind.Object
                        && j.Value.TryGetProperty("type", out var t)
                        && t.GetString() == "result")
            .LastOrDefault();

        if (lastResult is null)
            return new(false, rawOutput, 0, [], 0, 0, null);

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
        "haiku" => "claude-haiku-4-5",
        "sonnet" => "claude-sonnet-4-6",
        "opus" => "claude-opus-4-6",
        _ => alias
    };

    private static string Escape(string value) => value.Replace("'", "'\\''");
    private static string EscapeWindows(string value) => value.Replace("\"", "\\\"");

    private static JsonElement? TryParseJson(string line)
    {
        try { return JsonDocument.Parse(line).RootElement.Clone(); }
        catch { return null; }
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
