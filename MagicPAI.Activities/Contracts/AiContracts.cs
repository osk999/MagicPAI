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
    int ComplexityThreshold = 7,
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
