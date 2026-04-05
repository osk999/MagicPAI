using System.Text.Json;
using MagicPAI.Core.Models;

namespace MagicPAI.Core.Services;

public class ClaudeRunner : ICliAgentRunner
{
    public string AgentName => "claude";
    public string DefaultModel => "sonnet";
    public string[] AvailableModels => ["haiku", "sonnet", "opus"];

    public string BuildCommand(string prompt, string model, int maxTurns, string workDir)
    {
        var escapedPrompt = prompt.Replace("'", "'\\''");
        return $"cd {workDir} && claude " +
               $"--dangerously-skip-permissions " +
               $"-p '{escapedPrompt}' " +
               $"--model claude-{ResolveModel(model)} " +
               $"--max-turns {maxTurns} " +
               $"--output-format stream-json";
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
        return new(
            Success: !r.TryGetProperty("is_error", out var e) || !e.GetBoolean(),
            Output: r.TryGetProperty("result", out var res) ? res.GetString() ?? "" : "",
            CostUsd: ExtractCost(r),
            FilesModified: ExtractFiles(r),
            InputTokens: ExtractTokens(r, "input"),
            OutputTokens: ExtractTokens(r, "output"),
            SessionId: r.TryGetProperty("session_id", out var sid) ? sid.GetString() : null);
    }

    private static string ResolveModel(string alias) => alias switch
    {
        "haiku" => "haiku-4-5-20251001",
        "sonnet" => "sonnet-4-6-20250627",
        "opus" => "opus-4-6-20250627",
        _ => alias
    };

    private static JsonElement? TryParseJson(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            return doc.RootElement.Clone();
        }
        catch
        {
            return null;
        }
    }

    private static decimal ExtractCost(JsonElement r)
    {
        if (r.TryGetProperty("cost_usd", out var c) && c.TryGetDecimal(out var cost))
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
