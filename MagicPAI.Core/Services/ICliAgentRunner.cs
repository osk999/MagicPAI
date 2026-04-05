using MagicPAI.Core.Models;

namespace MagicPAI.Core.Services;

public interface ICliAgentRunner
{
    /// <summary>Agent identifier: "claude", "codex", "gemini".</summary>
    string AgentName { get; }

    /// <summary>Default model alias for this agent.</summary>
    string DefaultModel { get; }

    /// <summary>Available model aliases.</summary>
    string[] AvailableModels { get; }

    /// <summary>Build the CLI command string to execute in a container.</summary>
    string BuildCommand(string prompt, string model, int maxTurns, string workDir);

    /// <summary>Parse the raw output from the CLI agent.</summary>
    CliAgentResponse ParseResponse(string rawOutput);
}
