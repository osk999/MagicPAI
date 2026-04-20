// MagicPAI.Activities/Contracts/VerifyContracts.cs
// Temporal activity input/output records for the Verification activity group.
// See temporal.md §7.5 for spec.
namespace MagicPAI.Activities.Contracts;

public record VerifyInput(
    string ContainerId,
    string WorkingDirectory,
    IReadOnlyList<string> EnabledGates,  // e.g. ["compile", "test", "coverage"]
    string WorkerOutput,                 // trailing output from the agent (for quality review)
    string? SessionId = null);

public record VerifyOutput(
    bool AllPassed,
    IReadOnlyList<string> FailedGates,
    string GateResultsJson);

public record RepairInput(
    string ContainerId,
    IReadOnlyList<string> FailedGates,
    string OriginalPrompt,
    string GateResultsJson,
    int AttemptNumber,
    int MaxAttempts);

public record RepairOutput(
    string RepairPrompt,
    bool ShouldAttemptRepair);      // false if AttemptNumber > MaxAttempts
