// MagicPAI.Workflows/Contracts/SmartImproveContracts.cs
// Workflow-level input/output records for SmartImproveWorkflow and the
// SmartIterativeLoopWorkflow it dispatches per fix burst.
//
// Value-object records that ALSO need to be visible to activities
// (DoneRubric, RubricItem, RubricFailure, VerifyHarnessOutput,
// FilesystemSnapshot, FilesystemDelta) live in
// MagicPAI.Activities.Contracts.SmartImproveActivityContracts so the
// activity methods can reference them without a backwards project reference.
//
// See newplan.md §1 (architecture), §2.3 (contracts), §4 (anti-reward-hacking).
namespace MagicPAI.Workflows.Contracts;

// ─── Top-level SmartImprove ───────────────────────────────────────────────

/// <summary>
/// Input for <c>SmartImproveWorkflow</c>. Drives a preprocess-then-oscillate
/// loop: profile → rubric → harness, then alternating fix-bursts and
/// external verification until both runs return clean for two consecutive
/// cycles, or a hard cap fires.
/// </summary>
/// <remarks>
/// The burst schedule defaults to <c>[8, 8, 5, 5, 5, …]</c> with a steady-state
/// of 5. This is shorter than user intuition because Self-Refine
/// (arXiv 2303.17651) shows refinement regressions after iter ~3-5 without
/// external grounding, and AlphaCodium (arXiv 2401.08500) achieves SOTA with
/// only 15-20 LLM calls per problem total. See newplan.md §5 for the full
/// rationale.
/// </remarks>
public record SmartImproveInput(
    string SessionId,
    string Prompt,
    string AiAssistant,
    string WorkspacePath = "/workspace",
    /// <summary>Override schedule. <c>null</c> uses [8,8,5,5,5,…] up to MaxBursts.</summary>
    int[]? BurstSchedule = null,
    int SteadyStateBurstSize = 5,
    int MaxTotalIterations = 200,
    decimal MaxTotalBudgetUsd = 50m,
    int MaxBursts = 30,
    /// <summary>Consecutive clean verifier cycles required to terminate.</summary>
    int RequiredCleanVerifies = 2,
    /// <summary>Iterations of empty filesystem delta required after [DONE] to confirm.</summary>
    int SilenceCountdownIterations = 2,
    /// <summary>v2 feature — generate N candidate patches per stuck issue. Default off.</summary>
    bool EnableMultiCandidate = false,
    bool EnableGui = false);

public record SmartImproveOutput(
    /// <summary><c>"verified-clean" | "no-progress" | "budget" | "max-total" | "cancelled"</c></summary>
    string ExitReason,
    int IterationsRun,
    int BurstsCompleted,
    decimal TotalCostUsd,
    DoneRubricSnapshot FinalRubric,
    IReadOnlyList<string> RemainingP2P3Items);

// ─── Per-burst SmartIterativeLoop ─────────────────────────────────────────

/// <summary>
/// Input for the per-burst child workflow. Receives the parent's container
/// (no spawn/destroy here — parent owns the container for the entire run).
/// </summary>
public record SmartIterativeLoopInput(
    string SessionId,
    string ContainerId,
    string WorkspacePath,
    string Prompt,
    string AiAssistant,
    string? Model,
    int ModelPower,
    int MaxIterations,
    int MinIterations = 2,

    // Smart-termination feature toggles (all default ON for SmartImprove,
    // can be flipped off by other callers if they want vanilla loop semantics).
    bool UseGitNoProgressGuard = true,
    bool UseAstHashGuard = true,
    bool UseTestFailureSetGuard = true,
    bool UseSilenceCountdown = true,
    bool UseTestsTripwire = true,
    bool UseQuestionGuard = true,

    string CompletionMarker = "[DONE]",
    int SilenceCountdownIterations = 2,
    /// <summary>Iterations of no-progress before the burst exits early.</summary>
    int NoProgressThreshold = 3,
    /// <summary>How many of {git, AST, test-failure-set} must agree to count as no-progress.</summary>
    int NoProgressSignalsRequired = 2,
    decimal MaxBudgetUsd = 0m);

public record SmartIterativeLoopOutput(
    int IterationsRun,
    /// <summary><c>"silence-confirmed" | "no-progress" | "max-iterations" | "budget" | "signal" | "max-iter-during-silence"</c></summary>
    string ExitReason,
    bool DoneSignalled,
    bool SilenceConfirmed,
    /// <summary>True if the model edited files under tests/, *.spec.*, or *.test.* during this burst.</summary>
    bool TestsTripped,
    decimal TotalCostUsd,
    IReadOnlyList<string> ModifiedFiles,
    string? FinalResponse);

/// <summary>
/// Snapshot of rubric pass/fail counts at termination time. Goes back to
/// the API caller as part of <see cref="SmartImproveOutput"/>.
/// </summary>
public record DoneRubricSnapshot(
    int TotalItems,
    int PassedItems,
    int FailedP0,
    int FailedP1,
    int FailedP2,
    int FailedP3);
