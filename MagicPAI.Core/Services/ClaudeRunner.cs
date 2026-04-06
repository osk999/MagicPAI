using System.Text.Json;
using MagicPAI.Core.Models;

namespace MagicPAI.Core.Services;

public class ClaudeRunner : ICliAgentRunner
{
    public string AgentName => "claude";
    public string DefaultModel => "sonnet";
    public string[] AvailableModels => ["haiku", "sonnet", "opus"];
    public bool SupportsNativeSchema => true; // --json-schema flag

    public string BuildCommand(AgentRequest request)
    {
        var model = ResolveModel(request.Model ?? DefaultModel);
        var isWindows = OperatingSystem.IsWindows();

        // Platform-aware quoting: Windows cmd uses double quotes, bash uses single
        var q = isWindows ? "\"" : "'";
        var prompt = isWindows ? EscapeWindows(request.Prompt) : Escape(request.Prompt);

        // cd path: don't quote on Windows (cmd.exe doesn't handle quoted cd with forward slashes)
        var cdPath = isWindows ? request.WorkDir : $"'{request.WorkDir}'";
        var cmd = $"cd {cdPath} && claude" +
                  $" --dangerously-skip-permissions" +
                  $" -p {q}{prompt}{q}" +
                  $" --model claude-{model}" +
                  $" --output-format stream-json" +
                  $" --verbose";

        if (request.MaxBudgetUsd > 0)
            cmd += $" --max-budget-usd {request.MaxBudgetUsd:F2}";

        if (!string.IsNullOrEmpty(request.OutputSchema))
        {
            var schema = isWindows ? EscapeWindows(request.OutputSchema) : Escape(request.OutputSchema);
            cmd += $" --json-schema {q}{schema}{q}";
        }

        return cmd;
    }

    public CliAgentResponse ParseResponse(string rawOutput)
    {
        var lines = rawOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var lastResult = lines
            .Select(TryParseJson)
            .Where(j => j is not null
                        && j.Value.TryGetProperty("type", out var t)
                        && t.GetString() == "result")
            .LastOrDefault();

        if (lastResult is null)
            return new(false, rawOutput, 0, [], 0, 0, null);

        var r = lastResult.Value;

        // Extract structured_output if present (from --json-schema)
        var output = r.TryGetProperty("structured_output", out var so) && so.ValueKind != JsonValueKind.Null
            ? so.GetRawText()
            : r.TryGetProperty("result", out var res) ? res.GetString() ?? "" : "";

        return new(
            Success: !r.TryGetProperty("is_error", out var e) || !e.GetBoolean(),
            Output: output,
            CostUsd: ExtractCost(r),
            FilesModified: ExtractFiles(r),
            InputTokens: ExtractTokens(r, "input"),
            OutputTokens: ExtractTokens(r, "output"),
            SessionId: r.TryGetProperty("session_id", out var sid) ? sid.GetString() : null);
    }

    private static string ResolveModel(string alias) => alias switch
    {
        "haiku" => "haiku-4-5",
        "sonnet" => "sonnet-4-6",
        "opus" => "opus-4-6",
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
