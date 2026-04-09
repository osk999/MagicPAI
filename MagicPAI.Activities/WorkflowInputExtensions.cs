using Elsa.Extensions;
using Elsa.Workflows;

namespace MagicPAI.Activities;

internal static class WorkflowInputExtensions
{
    public static T? GetOptionalWorkflowInput<T>(
        this ActivityExecutionContext context,
        string name)
    {
        try
        {
            return context.GetWorkflowInput<T>(name);
        }
        catch (KeyNotFoundException)
        {
            return default;
        }
        catch (InvalidOperationException)
        {
            return default;
        }
    }
}
