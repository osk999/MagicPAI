using MagicPAI.Core.Models;

namespace MagicPAI.Core.Services;

public class GeminiRunner : ICliAgentRunner
{
    public string AgentName => "gemini";
    public string DefaultModel => "gemini-3.1-pro-preview";
    public string[] AvailableModels =>
        ["gemini-3.1-pro-preview", "gemini-3-flash", "gemini-3.1-flash-lite-preview",
         "gemini-2.5-pro", "gemini-2.5-flash"];

    public string BuildCommand(string prompt, string model, int maxTurns, string workDir)
    {
        var escaped = prompt.Replace("'", "'\\''");
        return $"cd {workDir} && gemini " +
               $"--model {ResolveModel(model)} " +
               $"--sandbox=false " +
               $"'{escaped}'";
    }

    public CliAgentResponse ParseResponse(string rawOutput) =>
        new(true, rawOutput, 0, [], 0, 0, null);

    private static string ResolveModel(string alias) => alias switch
    {
        "pro" => "gemini-3.1-pro-preview",
        "flash" => "gemini-3-flash",
        "flash-lite" => "gemini-3.1-flash-lite-preview",
        _ => alias
    };
}
