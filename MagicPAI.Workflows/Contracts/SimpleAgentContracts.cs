// MagicPAI.Workflows/Contracts/SimpleAgentContracts.cs
// Temporal workflow input/output for SimpleAgentWorkflow.
// See temporal.md §8.3 / §E.2.
namespace MagicPAI.Workflows.Contracts;

public record SimpleAgentInput(
    string SessionId,
    string Prompt,
    string AiAssistant,
    string? Model,
    int ModelPower,
    string WorkspacePath,
    bool EnableGui = true,
    IReadOnlyList<string>? EnabledGates = null,   // null => default gate set
    int MaxCoverageIterations = 3,
    // When set (non-empty), SimpleAgentWorkflow reuses this container instead of
    // spawning and destroying its own. Required when invoked as a child workflow
    // of an orchestrator that already owns a container — spawning a second
    // container would cause port collisions (noVNC 6080) and tear down the run.
    // Top-level SimpleAgent dispatches leave this null so the workflow behaves
    // as a self-contained unit (spawn + destroy in finally).
    string? ExistingContainerId = null,
    // Fast-path optimization: when true, skip the requirements-coverage loop
    // entirely if all verification gates pass on the first try. This saves
    // ~5-10s per successful run by avoiding a GradeCoverage Claude call for
    // tasks that already verified clean. Defaults false to preserve the
    // original always-check-coverage behavior.
    bool SkipCoverageWhenGatesPass = false);

public record SimpleAgentOutput(
    string Response,
    bool VerificationPassed,
    int CoverageIterations,
    decimal TotalCostUsd,
    IReadOnlyList<string> FilesModified);
