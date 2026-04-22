namespace MagicPAI.Activities.AI;

/// <summary>
/// Pure-function helpers for computing the session-map key an AI assistant
/// should use to look up its resume token. Originally held Elsa-context
/// getters/setters; those were removed when Temporal activities took over
/// session persistence through workflow state.
/// </summary>
internal static class AssistantSessionState
{
    private const string SessionKeySeparator = "::";

    internal static string CreateSessionMapKey(string assistantName, string? activityId)
    {
        var normalizedAssistant = assistantName?.Trim() ?? "";
        var normalizedActivityId = activityId?.Trim();

        return string.IsNullOrWhiteSpace(normalizedActivityId)
            ? normalizedAssistant
            : $"{normalizedAssistant}{SessionKeySeparator}{normalizedActivityId}";
    }
}
