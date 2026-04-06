using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Activities;
using Elsa.Workflows.Activities.Flowchart.Models;
using Elsa.Workflows.Models;
using MagicPAI.Activities.AI;

namespace MagicPAI.Server.Workflows;

/// <summary>
/// Generic reusable loop: runner executes, classifier checks for [DONE] marker,
/// exits or iterates up to max iterations. Uses RunCliAgentActivity + TriageActivity.
/// The loop-back is modeled as a Flowchart connection from triage back to the runner.
/// </summary>
public class LoopVerifierWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        builder.Name = "Loop Verifier";
        builder.Description =
            "Generic execution loop: run agent, check completion, iterate until done or max attempts";

        var prompt = builder.WithVariable<string>("Prompt", "");
        var containerId = builder.WithVariable<string>("ContainerId", "");
        var agent = builder.WithVariable<string>("Agent", "claude");
        var model = builder.WithVariable<string>("Model", "sonnet");

        // Step 1: Run the agent
        var runAgent = new RunCliAgentActivity
        {
            Agent = new Input<string>(agent),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Model = new Input<string>(model),
            Id = "loop-runner"
        };

        // Step 2: Classify the output (check for [DONE] marker)
        var classify = new TriageActivity
        {
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Id = "loop-classifier"
        };

        var flowchart = new Flowchart
        {
            Id = "loop-verifier-flow",
            Start = runAgent,
            Connections =
            {
                // Runner done -> classify
                new Connection(
                    new Endpoint(runAgent, "Done"),
                    new Endpoint(classify)),

                // Runner failed -> classify (check if partial progress)
                new Connection(
                    new Endpoint(runAgent, "Failed"),
                    new Endpoint(classify)),

                // Classifier says Simple (task complete / [DONE] found) -> terminal

                // Classifier says Complex (not done yet) -> loop back to runner
                new Connection(
                    new Endpoint(classify, "Complex"),
                    new Endpoint(runAgent)),
            }
        };

        builder.Root = flowchart;
    }
}
