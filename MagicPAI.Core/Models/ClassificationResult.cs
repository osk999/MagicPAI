namespace MagicPAI.Core.Models;

public record ClassificationResult(
    bool Result,
    int Confidence,
    string Rationale);
