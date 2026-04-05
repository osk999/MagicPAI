using MagicPAI.Core.Models;

namespace MagicPAI.Core.Services;

public class CodexRunner : ICliAgentRunner
{
    public string AgentName => "codex";
    public string DefaultModel => "gpt-5.4";
    public string[] AvailableModels =>
        ["gpt-5.4", "gpt-5.4-mini", "gpt-5.3-codex", "codex-mini-latest", "o3", "o4-mini", "gpt-4.1"];

    public string BuildCommand(string prompt, string model, int maxTurns, string workDir)
    {
        var escaped = prompt.Replace("'", "'\\''");
        return $"cd {workDir} && codex " +
               $"--approval-mode full-auto " +
               $"-m {ResolveModel(model)} " +
               $"'{escaped}'";
    }

    public CliAgentResponse ParseResponse(string rawOutput) =>
        new(!rawOutput.Contains("error", StringComparison.OrdinalIgnoreCase),
            rawOutput, 0, [], 0, 0, null);

    private static string ResolveModel(string alias) => alias switch
    {
        "gpt5" => "gpt-5.4",
        "gpt5-mini" => "gpt-5.4-mini",
        "codex" => "gpt-5.3-codex",
        "codex-mini" => "codex-mini-latest",
        _ => alias
    };
}
