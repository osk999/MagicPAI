using System.Text.Json;
using Elsa.Extensions;
using Elsa.Workflows;

namespace MagicPAI.Activities.AI;

internal static class AssistantSessionState
{
    private const string AssistantSessionMapVariable = "AiAssistantConversationSessionsJson";
    private const string SessionKeySeparator = "::";

    public static string? GetSessionId(ActivityExecutionContext context, string assistantName)
    {
        var raw = TryGetVariable<string>(context, AssistantSessionMapVariable);
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(raw) ?? new Dictionary<string, string>();
            var key = CreateSessionMapKey(assistantName, context.Activity.Id);
            return map.TryGetValue(key, out var sessionId) && !string.IsNullOrWhiteSpace(sessionId)
                ? sessionId
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string? GetOrCreateSessionId(ActivityExecutionContext context, string assistantName)
    {
        // Only return a session ID if one was previously stored from a real agent response.
        // Don't fabricate a new one — the CLI agent will create its own session ID and we
        // capture it from the response for subsequent resume calls.
        return GetSessionId(context, assistantName);
    }

    public static void SetSessionId(ActivityExecutionContext context, string assistantName, string sessionId)
    {
        Dictionary<string, string> map;
        var raw = TryGetVariable<string>(context, AssistantSessionMapVariable);

        try
        {
            map = string.IsNullOrWhiteSpace(raw)
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : JsonSerializer.Deserialize<Dictionary<string, string>>(raw)
                  ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        map[CreateSessionMapKey(assistantName, context.Activity.Id)] = sessionId;
        context.SetVariable(AssistantSessionMapVariable, JsonSerializer.Serialize(map));
    }

    internal static string CreateSessionMapKey(string assistantName, string? activityId)
    {
        var normalizedAssistant = assistantName?.Trim() ?? "";
        var normalizedActivityId = activityId?.Trim();

        return string.IsNullOrWhiteSpace(normalizedActivityId)
            ? normalizedAssistant
            : $"{normalizedAssistant}{SessionKeySeparator}{normalizedActivityId}";
    }

    private static T? TryGetVariable<T>(ActivityExecutionContext context, string name)
    {
        try
        {
            return context.GetVariable<T>(name);
        }
        catch
        {
            return default;
        }
    }
}
