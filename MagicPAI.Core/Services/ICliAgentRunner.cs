using MagicPAI.Core.Models;

namespace MagicPAI.Core.Services;

/// <summary>
/// Request object for running a CLI agent. All escaping, permissions,
/// and structured output are handled automatically by the runner.
/// </summary>
public record AgentRequest
{
    /// <summary>The user prompt / task description.</summary>
    public required string Prompt { get; init; }

    /// <summary>Model alias ("haiku", "sonnet", "opus") or full name. Null = agent default.</summary>
    public string? Model { get; init; }

    /// <summary>Working directory inside the container.</summary>
    public string WorkDir { get; init; } = "/workspace";

    /// <summary>Max budget in USD. 0 = unlimited (Claude only).</summary>
    public decimal MaxBudgetUsd { get; init; }

    /// <summary>
    /// Existing assistant conversation session to resume.
    /// When null, the runner should start a new session.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// JSON Schema string for structured output. When set, the agent is
    /// instructed to return JSON matching this schema.
    /// Use <see cref="AgentRequest.ForStructuredOutput{T}"/> to auto-generate from a C# type.
    /// </summary>
    public string? OutputSchema { get; init; }
}

public interface ICliAgentRunner
{
    /// <summary>Agent identifier: "claude", "codex", "gemini".</summary>
    string AgentName { get; }

    /// <summary>Default model alias for this agent.</summary>
    string DefaultModel { get; }

    /// <summary>Available model aliases.</summary>
    string[] AvailableModels { get; }

    /// <summary>
    /// True if the CLI tool enforces JSON Schema natively (guaranteed valid JSON).
    /// False if schema is embedded in the prompt (best-effort, may need fallback parsing).
    /// Claude: true (--json-schema), Codex: true (--output-schema), Gemini: false.
    /// </summary>
    bool SupportsNativeSchema { get; }

    /// <summary>
    /// Build the full CLI command string. Handles escaping, permissions,
    /// model resolution, and structured output per-agent automatically.
    /// </summary>
    string BuildCommand(AgentRequest request);

    /// <summary>
    /// Build a structured execution plan for safe argv-based execution.
    /// </summary>
    CliAgentExecutionPlan BuildExecutionPlan(AgentRequest request);

    /// <summary>Parse the raw CLI output into a structured response.</summary>
    CliAgentResponse ParseResponse(string rawOutput);
}
