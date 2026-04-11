using System.Text.Json;
using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Models;

namespace MagicPAI.Activities.AI;

internal static class ActivityHelpers
{
    public static string? Optional(Input<string>? input, ActivityExecutionContext context)
    {
        if (input is null)
            return null;

        string? value;
        try
        {
            value = input.Get(context);
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public static T GetOrDefault<T>(Input<T>? input, ActivityExecutionContext context, T fallback)
    {
        if (input is null)
            return fallback;

        try
        {
            return input.Get(context);
        }
        catch (InvalidOperationException)
        {
            return fallback;
        }
    }

    public static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    public static int? FirstNonZero(params int?[] values)
    {
        foreach (var v in values)
        {
            if (v.HasValue && v.Value > 0)
                return v.Value;
        }

        return null;
    }

    public static string? TryGetJson(string output)
    {
        var trimmed = output.Trim();
        if (trimmed.Length == 0)
            return null;
        if (!(trimmed.StartsWith('{') || trimmed.StartsWith('[')))
            return null;

        try
        {
            using var _ = JsonDocument.Parse(trimmed);
            return trimmed;
        }
        catch
        {
            return null;
        }
    }

    public static T? TryGetVariable<T>(ActivityExecutionContext context, string name)
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

    public static string Truncate(string? value, int maxLength = 4000)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    /// <summary>
    /// Resolve container ID from activity input, workflow variable, or workflow input.
    /// Three-level fallback: input -> variable -> workflow input.
    /// </summary>
    public static string ResolveContainerId(Input<string> containerIdInput, ActivityExecutionContext context)
    {
        var cid = GetOrDefault(containerIdInput, context, "");
        if (string.IsNullOrEmpty(cid))
            cid = TryGetVariable<string>(context, "ContainerId") ?? "";
        if (string.IsNullOrEmpty(cid))
            cid = context.GetOptionalWorkflowInput<string>("ContainerId") ?? "";
        if (string.IsNullOrWhiteSpace(cid))
            throw new InvalidOperationException("Container ID is required.");
        return cid;
    }

    /// <summary>
    /// Resolve prompt from activity input, workflow variable, or workflow input.
    /// </summary>
    public static string ResolvePrompt(Input<string> promptInput, ActivityExecutionContext context)
    {
        return FirstNonEmpty(
            Optional(promptInput, context),
            TryGetVariable<string>(context, "RepairPrompt"),
            TryGetVariable<string>(context, "Prompt"),
            context.GetOptionalWorkflowInput<string>("Prompt"),
            "") ?? "";
    }
}
