// MagicPAI.Activities/Contracts/AiContracts.cs
// Temporal activity input/output records for the AI activity group.
// Day 3 scope: RunCliAgent{Input,Output}.
// Day 4 scope: Triage, Classify, RouteModel, EnhancePrompt contracts.
// Day 5+ adds Architect / ResearchPrompt / WebsiteClassify / Coverage.
// See temporal.md §7.2 for the complete future shape.
namespace MagicPAI.Activities.Contracts;

public record RunCliAgentInput(
    string Prompt,
    string ContainerId,
    string AiAssistant,
    string? Model,
    int ModelPower,                 // 0=unspecified, 1=strongest, 2=balanced, 3=fastest
    string WorkingDirectory = "/workspace",
    string? StructuredOutputSchema = null,
    int MaxTurns = 20,
    int InactivityTimeoutMinutes = 30,
    string? SessionId = null,        // for SignalR streaming side channel (NOT the Claude CLI session)
    string? AssistantSessionId = null); // Claude/Codex/Gemini CLI session UUID for --resume on subsequent calls

public record RunCliAgentOutput(
    string Response,
    string? StructuredOutputJson,
    bool Success,
    decimal CostUsd,
    long InputTokens,
    long OutputTokens,
    IReadOnlyList<string> FilesModified,
    int ExitCode,
    string? AssistantSessionId);    // persisted for session resumption

// ── Day 4 ──────────────────────────────────────────────────────────────

public record TriageInput(
    string Prompt,
    string ContainerId,
    string? ClassificationInstructions,
    string AiAssistant,
    int ComplexityThreshold = 3,
    string? SessionId = null);

public record TriageOutput(
    int Complexity,
    string Category,                // "code_gen" | "bug_fix" | "refactor" | ...
    string RecommendedModel,
    int RecommendedModelPower,
    bool NeedsDecomposition,
    bool IsComplex);                // derived: Complexity >= threshold

public record ClassifierInput(
    string Prompt,
    string ClassificationQuestion,
    string ContainerId,
    int ModelPower,
    string AiAssistant,
    string? SessionId = null);

public record ClassifierOutput(
    bool Result,
    decimal Confidence,
    string Rationale);

public record RouteModelInput(
    string TaskCategory,            // from TriageOutput.Category
    int Complexity,
    string? PreferredAgent);        // override

public record RouteModelOutput(
    string SelectedAgent,
    string SelectedModel);

public record EnhancePromptInput(
    string OriginalPrompt,
    string EnhancementInstructions,
    string ContainerId,
    int ModelPower,
    string AiAssistant,
    string? SessionId = null);

public record EnhancePromptOutput(
    string EnhancedPrompt,
    bool WasEnhanced,
    string? Rationale);

// ── Day 5 ──────────────────────────────────────────────────────────────

public record ArchitectInput(
    string Prompt,
    string ContainerId,
    string? GapContext,
    string AiAssistant,
    string? SessionId = null);

public record ArchitectOutput(
    string TaskListJson,            // JSON-serialized TaskPlan
    int TaskCount,
    IReadOnlyList<TaskPlanEntry> Tasks);

public record TaskPlanEntry(
    string Id,
    string Description,
    IReadOnlyList<string> DependsOn,
    IReadOnlyList<string> FilesTouched);

public record ResearchPromptInput(
    string Prompt,
    string AiAssistant,
    string ContainerId,
    int ModelPower,
    string? SessionId = null);

public record ResearchPromptOutput(
    string EnhancedPrompt,
    string CodebaseAnalysis,
    string ResearchContext,
    string Rationale);

public record WebsiteClassifyInput(
    string Prompt,
    string ContainerId,
    string AiAssistant,
    string? SessionId = null);

public record WebsiteClassifyOutput(
    bool IsWebsiteTask,
    decimal Confidence,
    string Rationale);

public record CoverageInput(
    string OriginalPrompt,
    string ContainerId,
    string WorkingDirectory,
    int MaxIterations,
    int CurrentIteration,
    int ModelPower,
    string AiAssistant,
    string? SessionId = null);

public record CoverageOutput(
    bool AllMet,
    string GapPrompt,
    string CoverageReportJson,
    int Iteration);

// ── SmartImprove activities ────────────────────────────────────────────
// See newplan.md §2.2 (activities), §3 (verification harness),
// §4 (anti-reward-hacking).

public record GenerateRubricInput(
    string SessionId,
    string ContainerId,
    string WorkspacePath,
    /// <summary>The PROJECT_PROFILE.md content from ContextGatherer.</summary>
    string ProjectProfile,
    /// <summary>Original user prompt — anchors the rubric to user intent, not pure repo state.</summary>
    string OriginalPrompt,
    string AiAssistant,
    int ModelPower = 2);

public record GenerateRubricOutput(
    /// <summary>Detected project type — informs harness templates downstream.</summary>
    string ProjectType,
    string Rationale,
    /// <summary>JSON-serialized DoneRubric for cross-project transport.</summary>
    string RubricJson,
    int RubricItemCount,
    decimal CostUsd);

public record PlanVerificationHarnessInput(
    string SessionId,
    string ContainerId,
    string WorkspacePath,
    string ProjectType,
    /// <summary>JSON-serialized DoneRubric.</summary>
    string RubricJson,
    string AiAssistant,
    int ModelPower = 2);

public record PlanVerificationHarnessOutput(
    /// <summary>Bash-runnable harness script saved to /workspace/.smartimprove/harness.sh</summary>
    string HarnessScriptPath,
    /// <summary>Per-rubric-item command map. Key = rubric item id, value = command.</summary>
    IReadOnlyDictionary<string, string> CommandsByRubricId,
    decimal CostUsd);

public record PickNextImprovementInput(
    string SessionId,
    string ContainerId,
    string WorkspacePath,
    /// <summary>JSON-serialized current backlog (IMPROVEMENTS.md state).</summary>
    string BacklogJson,
    /// <summary>Most recent verifier failures — drives prioritization.</summary>
    string LatestFailuresJson,
    string AiAssistant,
    int ModelPower = 3);

public record PickNextImprovementOutput(
    /// <summary>True when no actionable item remains. SmartImprove treats this as a termination hint.</summary>
    bool BacklogEmpty,
    string? PickedItemId,
    string? Priority,
    /// <summary>Concrete prompt to feed into FullOrchestrate for this iteration.</summary>
    string? ImprovementPrompt,
    decimal CostUsd);

public record UpdateBacklogInput(
    string SessionId,
    string ContainerId,
    string WorkspacePath,
    string CurrentBacklogJson,
    string LatestVerifierOutputJson,
    /// <summary>What was just attempted, so we can mark items resolved or stuck.</summary>
    string? LastAttemptedItemId,
    string AiAssistant,
    int ModelPower = 3);

public record UpdateBacklogOutput(
    string UpdatedBacklogJson,
    int P0Count,
    int P1Count,
    int P2Count,
    int P3Count,
    int ResolvedThisRound,
    int NewlyDiscovered,
    decimal CostUsd);

public record VerifyHarnessInput(
    string SessionId,
    string ContainerId,
    string WorkspacePath,
    string HarnessScriptPath,
    /// <summary>JSON-serialized DoneRubric — needed for priority lookup per failed item.</summary>
    string RubricJson,
    /// <summary>True for the "second separated run" — triggers full clean rebuild + different seed.</summary>
    bool CleanRebuild = false,
    /// <summary>Random seed forwarded to harness for any tests that consume it.</summary>
    int Seed = 0,
    int TimeoutSeconds = 1800);

public record ClassifyFailuresInput(
    string SessionId,
    string ContainerId,
    string WorkspacePath,
    /// <summary>Raw harness output (stdout+stderr).</summary>
    string HarnessOutput,
    /// <summary>JSON-serialized failure list before classification.</summary>
    string FailuresJson,
    string AiAssistant,
    int ModelPower = 3);

public record ClassifyFailuresOutput(
    /// <summary>JSON-serialized list of <c>RubricFailure</c> with Classification populated.</summary>
    string ClassifiedFailuresJson,
    int RealCount,
    int StructuralCount,
    int EnvironmentalCount,
    decimal CostUsd);

public record SnapshotFilesystemInput(
    string ContainerId,
    string WorkspacePath,
    /// <summary>Glob patterns to exclude (in addition to .gitignore). Defaults: bin/, obj/, node_modules/, .git/.</summary>
    IReadOnlyList<string>? ExcludeGlobs = null,
    /// <summary>Hard cap on file count to keep snapshots fast on large repos.</summary>
    int MaxFiles = 50000);

public record SnapshotFilesystemOutput(
    /// <summary>Path → SHA-256 of contents (mtime/ctime ignored — content-only).</summary>
    IReadOnlyDictionary<string, string> FileHashes,
    long CapturedAtUnixSeconds,
    int FileCount,
    bool TruncatedByMaxFiles);

public record ComputeAstHashInput(
    string ContainerId,
    string WorkspacePath,
    /// <summary>Files to hash. If empty, every .cs under workspace.</summary>
    IReadOnlyList<string>? Files = null);

public record ComputeAstHashOutput(
    /// <summary>SHA-256 of the concatenated normalized AST hashes.</summary>
    string AstHash,
    int FilesHashed,
    /// <summary>True when no .cs files were found (workflow should fall back to git-only signal).</summary>
    bool NoCSharpFiles);

public record GetGitStateInput(
    string ContainerId,
    string WorkspacePath = "/workspace");

public record GetGitStateOutput(
    /// <summary>Current HEAD SHA, or empty if the workspace is not a git repo.</summary>
    string HeadSha,
    /// <summary>Number of changed entries reported by <c>git status --porcelain</c> (additions + modifications + deletions).</summary>
    int DirtyCount,
    /// <summary>True when <c>git status</c> reports nothing (clean working tree).</summary>
    bool IsClean,
    /// <summary>True when the workspace has no .git directory at all — workflow falls back to filesystem-delta signal.</summary>
    bool NotAGitRepo);
