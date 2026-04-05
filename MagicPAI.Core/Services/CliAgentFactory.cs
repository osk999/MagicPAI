namespace MagicPAI.Core.Services;

public class CliAgentFactory : ICliAgentFactory
{
    public string[] AvailableAgents => ["claude", "codex", "gemini"];

    public ICliAgentRunner Create(string agentName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName, nameof(agentName));

        return agentName switch
        {
            "claude" => new ClaudeRunner(),
            "codex" => new CodexRunner(),
            "gemini" => new GeminiRunner(),
            _ => throw new ArgumentException($"Unknown agent: '{agentName}'. Available: {string.Join(", ", AvailableAgents)}", nameof(agentName))
        };
    }
}
