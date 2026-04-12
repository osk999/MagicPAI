using System.Linq;
using Elsa.Workflows;

namespace MagicPAI.Server.Workflows;

public abstract class WorkflowBase : Elsa.Workflows.WorkflowBase
{
    /// <summary>
    /// Helper to set visual designer position on an activity.
    /// </summary>
    protected static void Pos(IActivity activity, int x, int y) =>
        activity.Metadata["designer"] = new { position = new { x, y } };

    protected override ValueTask AfterBuildAsync(IWorkflowBuilder builder, CancellationToken cancellationToken)
    {
        if (builder.Root is IVariableContainer variableContainer)
        {
            foreach (var variable in builder.Variables)
            {
                var exists = variableContainer.Variables.Any(existing =>
                    string.Equals(existing.Id, variable.Id, StringComparison.Ordinal) ||
                    string.Equals(existing.Name, variable.Name, StringComparison.Ordinal));

                if (!exists)
                    variableContainer.Variables.Add(variable);
            }
        }

        return base.AfterBuildAsync(builder, cancellationToken);
    }
}
