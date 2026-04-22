// MagicPAI.Workflows/Contracts/DeepResearchContracts.cs
// Temporal workflow input/output for DeepResearchOrchestrateWorkflow.
// See temporal.md §H.13. Composes ResearchPipeline + StandardOrchestrate.
namespace MagicPAI.Workflows.Contracts;

public record DeepResearchOrchestrateInput(
    string SessionId,
    string Prompt,
    string WorkspacePath,
    string AiAssistant,
    string? Model,
    int ModelPower,
    bool EnableGui = true);

public record DeepResearchOrchestrateOutput(
    string Response,
    bool VerificationPassed,
    string ResearchSummary,
    decimal TotalCostUsd);
