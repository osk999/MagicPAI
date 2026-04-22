// MagicPAI.Workflows/Contracts/WebsiteAuditContracts.cs
// Temporal workflow input/output for the website-audit workflows.
// See temporal.md §H.11 (WebsiteAuditCoreWorkflow) and §H.12 (WebsiteAuditLoopWorkflow).
namespace MagicPAI.Workflows.Contracts;

// --- §H.11 WebsiteAuditCoreWorkflow ------------------------------------------

public record WebsiteAuditCoreInput(
    string SessionId,
    string SectionId,
    string SectionDescription,
    string ContainerId,
    string WorkspacePath,
    string AiAssistant,
    string? Model);

public record WebsiteAuditCoreOutput(
    string SectionId,
    string AuditReport,
    int IssueCount,
    decimal CostUsd);

// --- §H.12 WebsiteAuditLoopWorkflow ------------------------------------------

public record WebsiteAuditInput(
    string SessionId,
    string ContainerId,
    string Prompt,
    string WorkspacePath,
    string AiAssistant,
    string? Model,
    IReadOnlyList<string>? SectionIds = null);

public record WebsiteAuditOutput(
    int SectionsAudited,
    int TotalIssueCount,
    string Summary,
    decimal CostUsd);
