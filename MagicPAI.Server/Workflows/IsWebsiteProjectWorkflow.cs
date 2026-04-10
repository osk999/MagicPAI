using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Activities;
using Elsa.Workflows.Activities.Flowchart.Models;
using Elsa.Workflows.Models;
using MagicPAI.Activities.AI;

namespace MagicPAI.Server.Workflows;

/// <summary>
/// Classifier sub-workflow: determines whether a task involves website auditing.
/// Uses TriageActivity with a website-detection oriented prompt.
/// Returns Simple (not a website project) or Complex (is a website project).
/// </summary>
public class IsWebsiteProjectWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        builder.Name = "Is Website Project";
        builder.Description =
            "Classifier that determines if a task involves website development or auditing";

        var prompt = builder.WithVariable<string>("Prompt", "");
        var containerId = builder.WithVariable<string>("ContainerId", "");

        var classify = new WebsiteTaskClassifierActivity
        {
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Id = "website-classifier"
        };

        var flowchart = new Flowchart
        {
            Id = "is-website-project-flow",
            Start = classify,
            Activities = { classify }
        };

        builder.Root = flowchart.WithAttachedVariables(builder);
    }
}
