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
    string? GapContext = null,
    // Cap on how many ComplexTaskWorkerWorkflow children may run concurrently
    // when the DAG ordering branch (Workflow.Patched("complex-path-dag-ordering-v1"))
    // is active. Default mirrors MagicPaiConfig.MaxConcurrentContainers (5).
    int MaxConcurrentWorkers = 5,
    // Base branch used by the worktree-per-task branch
    // (Workflow.Patched("complex-path-worktree-v1")) when creating worktrees.
    // "main" is the project default; callers can override per-session.
    string BaseBranch = "main",
    // Workspace directory inside the worker container that contains the .git
    // directory. Worktrees are created with `git -C {RepoDirectory} worktree add`.
    string RepoDirectory = "/workspace");

public record OrchestrateComplexOutput(
    int TaskCount,
    IReadOnlyList<ComplexTaskOutput> Results,
    decimal TotalCostUsd);
