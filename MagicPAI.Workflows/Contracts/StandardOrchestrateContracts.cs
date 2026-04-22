// MagicPAI.Workflows/Contracts/StandardOrchestrateContracts.cs
// Temporal workflow input/output for StandardOrchestrateWorkflow.
// See temporal.md §H.9.
namespace MagicPAI.Workflows.Contracts;

public record StandardOrchestrateInput(
    string SessionId,
    string Prompt,
    string WorkspacePath,
    string AiAssistant,
    string? Model,
    int ModelPower,
    bool EnableGui = true);

public record StandardOrchestrateOutput(
    string Response,
    bool VerificationPassed,
    decimal TotalCostUsd);
