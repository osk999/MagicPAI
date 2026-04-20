// MagicPAI.Workflows/Contracts/PromptGroundingContracts.cs
// Temporal workflow input/output for PromptGroundingWorkflow.
// See temporal.md §H.4.
namespace MagicPAI.Workflows.Contracts;

public record PromptGroundingInput(
    string SessionId,
    string Prompt,
    string ContainerId,
    string WorkingDirectory,
    string AiAssistant);

public record PromptGroundingOutput(
    string GroundedPrompt,
    string Rationale,
    decimal CostUsd);
