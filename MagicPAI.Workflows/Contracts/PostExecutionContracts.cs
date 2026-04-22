// MagicPAI.Workflows/Contracts/PostExecutionContracts.cs
// Temporal workflow input/output for PostExecutionPipelineWorkflow.
// See temporal.md §H.7.
namespace MagicPAI.Workflows.Contracts;

public record PostExecInput(
    string SessionId,
    string ContainerId,
    string WorkingDirectory,
    string AgentResponse,
    string AiAssistant);

public record PostExecOutput(
    bool ReportGenerated,
    string? ReportMarkdown,
    decimal CostUsd);
