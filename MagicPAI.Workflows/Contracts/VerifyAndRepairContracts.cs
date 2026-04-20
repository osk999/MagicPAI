// MagicPAI.Workflows/Contracts/VerifyAndRepairContracts.cs
// Temporal workflow input/output for VerifyAndRepairWorkflow.
// See temporal.md §H.1.
namespace MagicPAI.Workflows.Contracts;

/// <summary>
/// Input to the reusable verify-and-repair child workflow. The workflow reruns the
/// configured gates against the given container/working-directory and — on failure —
/// generates a repair prompt and invokes the agent again, up to <c>MaxRepairAttempts</c>.
/// </summary>
public record VerifyAndRepairInput(
    string SessionId,
    string ContainerId,
    string WorkingDirectory,
    string OriginalPrompt,
    string AiAssistant,
    string? Model,
    int ModelPower,
    IReadOnlyList<string> Gates,
    string WorkerOutput,
    int MaxRepairAttempts = 3);

public record VerifyAndRepairOutput(
    bool Success,
    int RepairAttempts,
    IReadOnlyList<string> FinalFailedGates,
    decimal RepairCostUsd);
