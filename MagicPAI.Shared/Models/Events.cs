namespace MagicPAI.Shared.Models;

/// <summary>Streaming agent output chunk.</summary>
public record OutputChunkEvent(string SessionId, string Text, string? ActivityName = null);

/// <summary>Overall workflow progress update.</summary>
public record WorkflowProgressEvent(
    string SessionId,
    string ActivityName,
    string Status,
    int CompletedSteps,
    int TotalSteps);

/// <summary>Verification gate result update.</summary>
public record VerificationUpdateEvent(
    string SessionId,
    string GateName,
    bool Passed,
    string Output,
    string[] Issues);

/// <summary>Cost tracking update.</summary>
public record CostUpdateEvent(
    string SessionId,
    decimal TotalCostUsd,
    int InputTokens,
    int OutputTokens);

/// <summary>Session state change.</summary>
public record SessionStateEvent(string SessionId, string State);

/// <summary>Container lifecycle event.</summary>
public record ContainerEvent(string SessionId, string ContainerId, string? GuiUrl);

/// <summary>Live container log line.</summary>
public record ContainerLogEvent(string SessionId, string ContainerId, string Line, DateTime TimestampUtc);

/// <summary>Error notification.</summary>
public record ErrorEvent(string SessionId, string Message, string? ActivityName = null);

/// <summary>Structured workflow insight for classifiers, prompt transforms, and repair steps.</summary>
public record TaskInsightEvent(
    string SessionId,
    string Kind,
    string Title,
    string Summary,
    string? Verdict,
    string? BeforeText,
    string? AfterText,
    string? RawPayload,
    DateTime TimestampUtc);

/// <summary>Session info for listing.</summary>
public class SessionInfo
{
    public string Id { get; set; } = "";
    public string? WorkflowId { get; set; }
    public string State { get; set; } = "idle";
    public decimal TotalCostUsd { get; set; }
    public string Agent { get; set; } = "claude";
    public string? ContainerId { get; set; }
    public string? GuiUrl { get; set; }
    public string? PromptPreview { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastActivityAt { get; set; }
    public string? LastActivityName { get; set; }
    public DateTime? LastOutputAt { get; set; }
    public DateTime? LastContainerLogAt { get; set; }
    public DateTime? LastUpdatedAt { get; set; }
}
