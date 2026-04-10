namespace MagicPAI.Core.Models;

public record WebsiteTaskClassificationResult(
    bool IsWebsiteTask,
    int Confidence,
    string Rationale);
