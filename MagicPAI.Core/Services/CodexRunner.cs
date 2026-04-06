using MagicPAI.Core.Models;

namespace MagicPAI.Core.Services;

public class CodexRunner : ICliAgentRunner
{
    public string AgentName => "codex";
    public string DefaultModel => "gpt-5.4";
    public string[] AvailableModels =>
        ["gpt-5.4", "gpt-5.4-mini", "gpt-5.3-codex", "codex-mini-latest", "o3", "o4-mini", "gpt-4.1"];
    public bool SupportsNativeSchema => true; // --output-schema flag

    public string BuildCommand(AgentRequest request)
    {
        var prompt = Escape(request.Prompt);
        var model = ResolveModel(request.Model ?? DefaultModel);

        var cmd = $"cd {request.WorkDir}";

        // Codex --output-schema needs a file; write schema to temp file first
        if (!string.IsNullOrEmpty(request.OutputSchema))
        {
            var schemaFile = $"/tmp/codex-schema-$$.json";
            cmd += $" && echo '{Escape(request.OutputSchema)}' > {schemaFile}";
        }

        cmd += $" && codex exec" +
               $" --sandbox danger-full-access" +
               $" -c 'ask_for_approval=\"never\"'" +
               $" -m {model}";

        if (!string.IsNullOrEmpty(request.OutputSchema))
            cmd += $" --output-schema /tmp/codex-schema-$$.json";

        cmd += $" '{prompt}'";
        return cmd;
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

    private static string Escape(string value) => value.Replace("'", "'\\''");
}
