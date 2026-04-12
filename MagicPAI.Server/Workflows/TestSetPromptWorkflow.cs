using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Activities;
using Elsa.Workflows.Activities.Flowchart.Models;
using Elsa.Workflows.Models;
using MagicPAI.Activities.AI;

namespace MagicPAI.Server.Workflows;

/// <summary>
/// Minimal test workflow for verifying prompt enhancement.
/// Simply runs a single AiAssistantActivity to test that the prompt enhancement
/// pipeline is functioning correctly.
/// </summary>
public class TestSetPromptWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        builder.Name = "Test Set Prompt";
        builder.Description = "Minimal test workflow for verifying prompt enhancement";

        var prompt = builder.WithVariable<string>("Prompt", "");
        var agent = builder.WithVariable<string>("AiAssistant", "claude");
        var containerId = builder.WithVariable<string>("ContainerId", "");

        var testAgent = new AiAssistantActivity
        {
            AiAssistant = new Input<string>(agent),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            ModelPower = new Input<int>(2),
            Id = "test-prompt-agent"
        };
        Pos(testAgent, 400, 150);

        var flowchart = new Flowchart
        {
            Id = "test-set-prompt-flow",
            Start = testAgent,
            Activities = { testAgent }
        };

        builder.Root = flowchart.WithAttachedVariables(builder);
    }
}
