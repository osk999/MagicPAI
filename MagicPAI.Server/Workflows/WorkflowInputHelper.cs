using Elsa.Extensions;
using Elsa.Expressions.Models;

namespace MagicPAI.Server.Workflows;

/// <summary>
/// Helpers to read workflow dispatch input bypassing Elsa's variable shadowing.
///
/// Elsa's ExpressionExecutionContext.GetInput(name) checks for a variable with
/// the same name FIRST (ExpressionExecutionContextExtensions.cs:418-425).
/// If a workflow declares builder.WithVariable("Prompt", ""), calling
/// ctx.GetInput("Prompt") returns "" instead of the dispatch input value.
///
/// These helpers read from WorkflowExecutionContext.Input directly.
/// </summary>
internal static class WorkflowInputHelper
{
    /// <summary>
    /// Read dispatch input bypassing variable shadowing.
    /// </summary>
    public static string? GetDispatchInput(this ExpressionExecutionContext ctx, string name)
    {
        var input = ctx.GetWorkflowExecutionContext().Input;
        return input.TryGetValue(name, out var v) ? v?.ToString() : null;
    }

    /// <summary>
    /// Read typed dispatch input bypassing variable shadowing.
    /// </summary>
    public static T? GetDispatchInput<T>(this ExpressionExecutionContext ctx, string name)
    {
        var input = ctx.GetWorkflowExecutionContext().Input;
        if (!input.TryGetValue(name, out var v)) return default;
        if (v is T typed) return typed;
        try { return (T)Convert.ChangeType(v, typeof(T)); }
        catch { return default; }
    }

    /// <summary>
    /// Resolve a string: variable (if non-empty) > dispatch input > fallback.
    /// Use this instead of ctx.GetInput() when variable names match input keys.
    /// </summary>
    public static string Resolve(this ExpressionExecutionContext ctx, string name, string fallback = "")
    {
        var fromVar = ctx.GetVariable<string>(name);
        if (!string.IsNullOrWhiteSpace(fromVar)) return fromVar;
        return ctx.GetDispatchInput(name) ?? fallback;
    }

    /// <summary>
    /// Resolve first non-empty value from multiple input key names.
    /// </summary>
    public static string ResolveFirst(this ExpressionExecutionContext ctx, string fallback, params string[] names)
    {
        foreach (var name in names)
        {
            var val = ctx.Resolve(name);
            if (!string.IsNullOrWhiteSpace(val)) return val;
        }
        return fallback;
    }
}
