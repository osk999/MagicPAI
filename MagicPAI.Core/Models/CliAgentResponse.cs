namespace MagicPAI.Core.Models;

public record CliAgentResponse(
    bool Success,
    string Output,
    decimal CostUsd,
    string[] FilesModified,
    int InputTokens,
    int OutputTokens,
    string? SessionId,
    string? StructuredOutputJson = null);
