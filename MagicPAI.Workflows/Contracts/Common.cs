// MagicPAI.Workflows/Contracts/Common.cs
// Shared workflow contract records — referenced by more than one workflow.
// See temporal.md §6.2 for the canonical shapes.
namespace MagicPAI.Workflows.Contracts;

/// <summary>
/// Model selection triple — which AI vendor + which specific model + power class.
/// <c>ModelPower</c>: 1=strongest, 2=balanced, 3=fastest, 0=unspecified.
/// </summary>
public record ModelSpec(
    string AiAssistant,         // "claude" | "codex" | "gemini"
    string? Model,              // "sonnet" | "opus" | "gpt-5.4" | ... | null for auto
    int ModelPower);            // 1=strongest, 2=balanced, 0=unspecified

/// <summary>
/// Session-level context passed to every workflow that owns a container / streams output.
/// </summary>
public record SessionContext(
    string SessionId,           // ties workflow to SignalR session for streaming
    string WorkspacePath,
    bool EnableGui);

/// <summary>
/// Declarative gate configuration. <c>Name</c> is a registered gate identifier
/// (e.g. "compile", "test", "coverage"); <c>Blocking</c> decides whether a failure
/// halts the workflow; <c>Config</c> carries gate-specific options.
/// </summary>
public record VerifyGateSpec(
    string Name,                // "compile" | "test" | "coverage" | ...
    bool Blocking,
    Dictionary<string, string> Config);

/// <summary>
/// Condensed verification result suitable for workflow state and Signal serialization.
/// The detailed gate payload lives in <c>GateResultsJson</c> so the workflow history stays
/// small. See temporal.md §6.3.
/// </summary>
public record VerifyResult(
    bool AllPassed,
    IReadOnlyList<string> FailedGates,
    string GateResultsJson);

/// <summary>
/// Per-activity cost tracking entry. Emitted to SignalR side channel by the activity
/// implementation rather than routed through workflow history to keep history bounded.
/// </summary>
public record CostEntry(
    string Agent,
    string Model,
    decimal CostUsd,
    long InputTokens,
    long OutputTokens);
