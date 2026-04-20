// MagicPAI.Activities/Contracts/BlackboardContracts.cs
// Temporal activity input/output records for the Blackboard activity group.
// See temporal.md §7.6 for spec.
namespace MagicPAI.Activities.Contracts;

public record ClaimFileInput(
    string FilePath,
    string TaskId,
    string SessionId);

public record ClaimFileOutput(
    bool Claimed,
    string? CurrentOwner);          // null if claimed successfully

public record ReleaseFileInput(
    string FilePath,
    string TaskId,
    string SessionId);
