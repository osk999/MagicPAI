// MagicPAI.Activities/Contracts/GitContracts.cs
// Temporal activity input/output records for the Git activity group.
// See temporal.md §7.4 for spec.
namespace MagicPAI.Activities.Contracts;

public record CreateWorktreeInput(
    string ContainerId,
    string BranchName,
    string RepoDirectory,
    string BaseBranch = "main");

public record CreateWorktreeOutput(
    string WorktreePath,
    bool CreatedFromScratch);       // false if branch already existed

public record MergeWorktreeInput(
    string ContainerId,
    string BranchName,
    string RepoDirectory,
    string TargetBranch = "main",
    bool PushAfterMerge = false);

public record MergeWorktreeOutput(
    bool Merged,
    string? ConflictReport,
    string? MergeCommitSha);

public record CleanupWorktreeInput(
    string ContainerId,
    string BranchName,
    string RepoDirectory,
    bool DeleteBranch = false);
