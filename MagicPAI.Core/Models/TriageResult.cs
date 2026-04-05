namespace MagicPAI.Core.Models;

public record TriageResult(
    int Complexity,
    string Category,
    string RecommendedModel,
    bool NeedsDecomposition);
