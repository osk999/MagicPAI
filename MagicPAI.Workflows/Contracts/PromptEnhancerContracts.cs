// MagicPAI.Workflows/Contracts/PromptEnhancerContracts.cs
// Temporal workflow input/output for PromptEnhancerWorkflow.
// See temporal.md §H.2.
namespace MagicPAI.Workflows.Contracts;

public record PromptEnhancerInput(
    string SessionId,
    string OriginalPrompt,
    string ContainerId,
    string AiAssistant,
    int ModelPower = 2,
    string? EnhancementInstructions = null,
    // Container-side working directory used when the workflow has to spawn its
    // own container (top-level HTTP dispatch with empty ContainerId). When the
    // caller already owns the container (non-empty ContainerId), this is unused.
    string WorkspacePath = "/workspace");

public record PromptEnhancerOutput(
    string EnhancedPrompt,
    bool WasEnhanced,
    string? Rationale,
    decimal CostUsd);
