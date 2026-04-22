// MagicPAI.Workflows/Contracts/IterativeLoopContracts.cs
// Reusable iteration component. Wraps a user prompt in a min/max-iteration
// loop with a structured completion protocol. Any workflow can dispatch this
// as a child workflow to get "run until done or max" semantics out of the box.
namespace MagicPAI.Workflows.Contracts;

/// <summary>
/// How the loop decides a turn was "done".
/// </summary>
public enum CompletionStrategy
{
    /// <summary>
    /// Check the last response for a literal marker on its own line (default
    /// <c>[DONE]</c>). Deterministic, free, no extra activity call.
    /// </summary>
    Marker,

    /// <summary>
    /// Ask <c>AiActivities.ClassifyAsync</c> whether the response indicates
    /// completion. Costs one cheap classifier call per iteration but tolerates
    /// prose drift around the marker.
    /// </summary>
    Classifier,

    /// <summary>
    /// Parse the structured progress report the coda demands (`- [ ]` vs
    /// `- [x]`, `Completion: true`, plus the marker). Most reliable — three
    /// independent signals must agree. Still deterministic/workflow-side.
    /// </summary>
    StructuredProgress,
}

public record IterativeLoopInput(
    string SessionId,
    string Prompt,
    string AiAssistant,
    string? Model,
    int ModelPower,

    // Loop shape
    int MinIterations = 1,
    int MaxIterations = 10,

    // Completion detection
    CompletionStrategy CompletionStrategy = CompletionStrategy.StructuredProgress,
    string CompletionMarker = "[DONE]",
    string? CompletionInstructions = null,   // classifier strategy only
    // StructuredProgress only: minimum number of tasks that must appear in
    // the `### Task Status` checklist before the loop will accept it as
    // done. Prevents the model from gaming the protocol with a trivial
    // one-item "done" checklist. Ignored by Marker/Classifier strategies.
    int MinRequiredTasks = 0,

    // Container handoff (same pattern as SimpleAgent / Fix #2+#125+#126)
    string WorkspacePath = "/workspace",
    string? ExistingContainerId = null,
    bool EnableGui = false,

    // Allow callers to replace the whole coda if they want a different format
    string? CodaOverride = null,

    // Optional hard budget cap in USD (0 disables)
    decimal MaxBudgetUsd = 0m);

public record IterativeLoopOutput(
    string FinalResponse,
    bool DoneMarkerObserved,
    int IterationsRun,
    decimal TotalCostUsd,
    /// <summary>
    /// <c>"done"</c> | <c>"max-iterations"</c> | <c>"budget"</c> | <c>"signal"</c>
    /// </summary>
    string ExitReason,
    /// <summary>
    /// Parsed structured progress on the final iteration, when the
    /// <c>StructuredProgress</c> strategy was used. Null otherwise.
    /// </summary>
    IterativeLoopProgress? FinalProgress = null);

public record IterativeLoopProgress(
    int TotalTasks,
    int CompletedTasks,
    bool CompletionFlag,
    bool MarkerPresent,
    IReadOnlyList<string> OpenTaskDescriptions);
