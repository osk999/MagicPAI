namespace MagicPAI.Core.Models;

public record TriageResult(
    int Complexity,
    string Category,
    int RecommendedModelPower,
    bool NeedsDecomposition);
