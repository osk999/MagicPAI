namespace MagicPAI.Core.Models;

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

/// <summary>Error notification.</summary>
public record ErrorEvent(string SessionId, string Message, string? ActivityName = null);

/// <summary>Session info for listing.</summary>
public class SessionInfo
{
    public string Id { get; set; } = "";
    public string? WorkflowId { get; set; }
    public string State { get; set; } = "idle";
    public decimal TotalCostUsd { get; set; }
    public string Agent { get; set; } = "claude";
    public string? ContainerId { get; set; }
    public string? PromptPreview { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
