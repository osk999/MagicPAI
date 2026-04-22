namespace MagicPAI.Shared.Hubs;

public record CostEntry(
    string SessionId,
    decimal IncrementUsd,
    decimal TotalUsd,
    string Agent,
    string Model,
    long InputTokens,
    long OutputTokens);

public record VerifyGateResult(
    string GateName,
    bool Passed,
    bool Blocking,
    string Summary,
    long DurationMs);

public record GateAwaitingPayload(
    string SessionId,
    string GateName,
    string PromptForHuman,
    IReadOnlyList<string> Options);

public record ContainerSpawnedPayload(
    string SessionId,
    string ContainerId,
    string? GuiUrl,
    string WorkspacePath);

public record ContainerDestroyedPayload(
    string SessionId,
    string ContainerId);

public record SessionCompletedPayload(
    string SessionId,
    string WorkflowType,
    DateTime CompletedAt,
    decimal TotalCostUsd,
    object? Result);

public record SessionFailedPayload(
    string SessionId,
    string ErrorMessage,
    string? ErrorType);

public record SessionCancelledPayload(
    string SessionId,
    string Reason);
