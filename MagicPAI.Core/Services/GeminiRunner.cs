using MagicPAI.Core.Models;

namespace MagicPAI.Core.Services;

public class GeminiRunner : ICliAgentRunner
{
    public string AgentName => "gemini";
    public string DefaultModel => "gemini-3.1-pro-preview";
    public string[] AvailableModels =>
    [
        "gemini-3.1-pro-preview", "gemini-3-flash", "gemini-3.1-flash-lite-preview",
        "gemini-2.5-pro", "gemini-2.5-flash",
        "pro", "flash", "flash-lite"
    ];
    public bool SupportsNativeSchema => false; // no CLI flag, schema embedded in prompt

    public string BuildCommand(AgentRequest request)
    {
        var prompt = request.Prompt ?? "";

        // Gemini CLI has no native --json-schema; embed in prompt
        if (!string.IsNullOrEmpty(request.OutputSchema))
            prompt = $"Respond with ONLY valid JSON matching this schema (no markdown, no explanation):\n{request.OutputSchema}\n\nTask: {prompt}";

        var escaped = Escape(prompt);
        var model = ResolveModel(request.Model ?? DefaultModel);

        return $"cd '{request.WorkDir ?? "/workspace"}' && gemini" +
               $" -p '{escaped}'" +
               $" --model {model}" +
               $" --yolo" +
               $" --output-format json";
    }

    public CliAgentExecutionPlan BuildExecutionPlan(AgentRequest request)
    {
        var prompt = request.Prompt ?? "";

        if (!string.IsNullOrEmpty(request.OutputSchema))
            prompt = $"Respond with ONLY valid JSON matching this schema (no markdown, no explanation):\n{request.OutputSchema}\n\nTask: {prompt}";

        var model = ResolveModel(request.Model ?? DefaultModel);

        return new CliAgentExecutionPlan(
            new ContainerExecRequest(
                FileName: "gemini",
                Arguments:
                [
                    "-p",
                    prompt,
                    "--model",
                    model,
                    "--yolo",
                    "--output-format",
                    "json"
                ],
                WorkingDirectory: request.WorkDir ?? "/workspace"));
    }

    public CliAgentResponse ParseResponse(string rawOutput) =>
        new(true, rawOutput, 0, [], 0, 0, null, TryExtractJson(rawOutput));

    private static string ResolveModel(string alias) => alias switch
    {
        "pro" => "gemini-3.1-pro-preview",
        "flash" => "gemini-3-flash",
        "flash-lite" => "gemini-3.1-flash-lite-preview",
        _ => alias
    };

    private static string Escape(string value) => value.Replace("'", "'\\''");

    private static string? TryExtractJson(string output)
    {
        var trimmed = output.Trim();
        if (trimmed.Length == 0)
            return null;
        if (!(trimmed.StartsWith('{') || trimmed.StartsWith('[')))
            return null;

        try
        {
            using var _ = System.Text.Json.JsonDocument.Parse(trimmed);
            return trimmed;
        }
        catch
        {
            return null;
        }
    }
}
