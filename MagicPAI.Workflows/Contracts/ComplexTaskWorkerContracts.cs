// MagicPAI.Workflows/Contracts/ComplexTaskWorkerContracts.cs
// Temporal workflow input/output for ComplexTaskWorkerWorkflow.
// See temporal.md §H.6. OrchestrateComplexPath dispatches one instance per
// decomposed task emitted by the Architect activity; this child claims the
// files it touches, runs the agent, and releases the claims in a finally block.
namespace MagicPAI.Workflows.Contracts;

public record ComplexTaskInput(
    string TaskId,
    string Description,
    IReadOnlyList<string> DependsOn,
    IReadOnlyList<string> FilesTouched,
    string ContainerId,
    string AiAssistant,
    string? Model,
    int ModelPower,
    string WorkspacePath,
    string ParentSessionId);

public record ComplexTaskOutput(
    string TaskId,
    bool Success,
    string Response,
    decimal CostUsd,
    IReadOnlyList<string> FilesModified,
    bool VerificationPassed = true);
