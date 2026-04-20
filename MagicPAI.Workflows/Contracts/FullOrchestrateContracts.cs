// MagicPAI.Workflows/Contracts/FullOrchestrateContracts.cs
// Temporal workflow input/output for FullOrchestrateWorkflow.
// See temporal.md §8.6 and §T.9. Central orchestrator — routes to website-audit,
// simple-path, or complex-path based on website classifier + triage verdict.
namespace MagicPAI.Workflows.Contracts;

public record FullOrchestrateInput(
    string SessionId,
    string Prompt,
    string WorkspacePath,
    string AiAssistant,
    string? Model,
    int ModelPower,
    bool EnableGui = true,
    // Complex-path coverage loop cap. 2 by default because the complex
    // branch is expensive (one re-run = one full agent invocation). Simple
    // path uses SimpleAgentInput.MaxCoverageIterations (default 3) which
    // runs inside the SimpleAgentWorkflow child.
    int MaxCoverageIterations = 2,
    // HITL (human-in-the-loop) triage gate. When true, the workflow pauses
    // after triage completes and before branch selection, awaiting an
    // ApproveGate or RejectGate signal. Defaults OFF so existing flows are
    // not blocked.
    bool RequireTriageApproval = false,
    // Hours to wait for a gate-approval signal before treating it as a
    // rejection. Only relevant when RequireTriageApproval=true.
    int GateApprovalTimeoutHours = 24,
    // Triage complexity threshold (1–10). When the triage activity rates
    // the prompt at or above this value, FullOrchestrate routes to the
    // complex-path branch (decomposition + per-subtask workers). Default 7
    // matches the pre-migration Elsa behavior. Lower values force more
    // prompts down the complex path — useful for demos/POC. Higher values
    // route more work through the simple-agent path.
    int ComplexityThreshold = 7);

public record FullOrchestrateOutput(
    string PipelineUsed,            // "website-audit" | "simple" | "complex"
    string FinalResponse,
    decimal TotalCostUsd);
