// MagicPAI.Workflows/Contracts/ContextGathererContracts.cs
// Temporal workflow input/output for ContextGathererWorkflow.
// See temporal.md §H.3.
namespace MagicPAI.Workflows.Contracts;

public record ContextGathererInput(
    string SessionId,
    string Prompt,
    string ContainerId,
    string WorkingDirectory,
    string AiAssistant,
    int MaxFiles = 30);

public record ContextGathererOutput(
    string GatheredContext,
    IReadOnlyList<string> ReferencedFiles,
    decimal CostUsd);
