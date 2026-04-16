namespace MagicPAI.Core.Models;

/// <summary>
/// Output of the requirements-coverage classifier. Grades the completed work
/// against the original user-stated requirements, item by item.
/// </summary>
public record CoverageResult(
    CoverageItem[] Requirements,
    bool AllMet,
    string GapPrompt,
    string Summary);

public record CoverageItem(
    string Id,
    string Description,
    string Status,
    string Evidence);
