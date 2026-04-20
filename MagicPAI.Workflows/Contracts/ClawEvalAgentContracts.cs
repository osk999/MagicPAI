// MagicPAI.Workflows/Contracts/ClawEvalAgentContracts.cs
// Temporal workflow input/output for ClawEvalAgentWorkflow.
// See temporal.md §H.10. Specialized for evaluation runs; preserves the Elsa
// workflow's behavior (run agent, then compile/test/coverage gates).
namespace MagicPAI.Workflows.Contracts;

public record ClawEvalAgentInput(
    string SessionId,
    string EvalTaskId,
    string Prompt,
    string ContainerId,
    string WorkspacePath,
    string AiAssistant,
    string? Model,
    int ModelPower);

public record ClawEvalAgentOutput(
    string Response,
    bool PassedEval,
    string EvalReport,
    decimal CostUsd);
