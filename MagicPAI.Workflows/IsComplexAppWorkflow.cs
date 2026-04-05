using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Activities;
using Elsa.Workflows.Activities.Flowchart.Models;
using Elsa.Workflows.Models;
using MagicPAI.Activities.AI;

namespace MagicPAI.Workflows;

/// <summary>
/// Classifier sub-workflow: determines whether a task needs multi-agent decomposition.
/// Returns Simple (no decomposition needed) or Complex (needs architect decomposition).
/// </summary>
public class IsComplexAppWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        builder.Name = "Is Complex App";
        builder.Description =
            "Classifier that determines if a task requires multi-agent decomposition";

        var prompt = builder.WithVariable<string>("Prompt", "");
        var containerId = builder.WithVariable<string>("ContainerId", "");

        var classify = new TriageActivity
        {
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Id = "complexity-classifier"
        };

        var flowchart = new Flowchart
        {
            Id = "is-complex-app-flow",
            Start = classify
        };

        builder.Root = flowchart;
    }
}
