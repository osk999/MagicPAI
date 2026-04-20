// MagicPAI.Workflows/Contracts/OrchestrateSimpleContracts.cs
// Temporal workflow input/output for OrchestrateSimplePathWorkflow.
// See temporal.md §H.5.
namespace MagicPAI.Workflows.Contracts;

public record OrchestrateSimpleInput(
    string SessionId,
    string Prompt,
    string ContainerId,
    string WorkspacePath,
    string AiAssistant,
    string? Model,
    int ModelPower,
    bool EnableGui = true);

public record OrchestrateSimpleOutput(
    string Response,
    bool VerificationPassed,
    decimal TotalCostUsd);
