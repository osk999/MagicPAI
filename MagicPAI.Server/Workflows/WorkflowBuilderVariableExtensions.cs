using System.Linq;
using Elsa.Workflows;

namespace MagicPAI.Server.Workflows;

internal static class WorkflowBuilderVariableExtensions
{
    public static IActivity WithAttachedVariables(this IActivity root, IWorkflowBuilder builder)
    {
        if (root is not IVariableContainer variableContainer)
            return root;

        foreach (var variable in builder.Variables)
        {
            var exists = variableContainer.Variables.Any(existing =>
                string.Equals(existing.Id, variable.Id, StringComparison.Ordinal) ||
                string.Equals(existing.Name, variable.Name, StringComparison.Ordinal));

            if (!exists)
                variableContainer.Variables.Add(variable);
        }

        return root;
    }
}
