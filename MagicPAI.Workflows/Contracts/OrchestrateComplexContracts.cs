// MagicPAI.Workflows/Contracts/OrchestrateComplexContracts.cs
// Temporal workflow input/output for OrchestrateComplexPathWorkflow.
// See temporal.md §8.5. The per-task ComplexTaskInput / ComplexTaskOutput records
// live in ComplexTaskWorkerContracts.cs (§H.6).
namespace MagicPAI.Workflows.Contracts;

public record OrchestrateComplexInput(
    string SessionId,
    string Prompt,
    string ContainerId,
    string WorkspacePath,
    string AiAssistant,
    string? Model,
    int ModelPower,
    string? GapContext = null);

public record OrchestrateComplexOutput(
    int TaskCount,
    IReadOnlyList<ComplexTaskOutput> Results,
    decimal TotalCostUsd);
