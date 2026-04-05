using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Activities;
using Elsa.Workflows.Activities.Flowchart.Models;
using Elsa.Workflows.Models;
using MagicPAI.Activities.AI;

namespace MagicPAI.Workflows;

/// <summary>
/// Minimal test workflow for verifying prompt enhancement.
/// Simply runs a single RunCliAgentActivity to test that the prompt enhancement
/// pipeline is functioning correctly.
/// </summary>
public class TestSetPromptWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        builder.Name = "Test Set Prompt";
        builder.Description = "Minimal test workflow for verifying prompt enhancement";

        var prompt = builder.WithVariable<string>("Prompt", "");
        var containerId = builder.WithVariable<string>("ContainerId", "");

        var testAgent = new RunCliAgentActivity
        {
            Agent = new Input<string>("claude"),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Model = new Input<string>("haiku"),
            Id = "test-prompt-agent"
        };

        var flowchart = new Flowchart
        {
            Id = "test-set-prompt-flow",
            Start = testAgent
        };

        builder.Root = flowchart;
    }
}
