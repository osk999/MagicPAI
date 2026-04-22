// MagicPAI.Workflows/Contracts/ResearchPipelineContracts.cs
// Temporal workflow input/output for ResearchPipelineWorkflow.
// See temporal.md §H.8.
namespace MagicPAI.Workflows.Contracts;

public record ResearchPipelineInput(
    string SessionId,
    string Prompt,
    string ContainerId,
    string WorkingDirectory,
    string AiAssistant);

public record ResearchPipelineOutput(
    string ResearchedPrompt,
    string ResearchContext,
    decimal CostUsd);
