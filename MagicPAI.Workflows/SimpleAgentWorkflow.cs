using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Activities;
using Elsa.Workflows.Activities.Flowchart.Models;
using Elsa.Workflows.Models;
using MagicPAI.Activities.AI;
using MagicPAI.Activities.Docker;
using MagicPAI.Activities.Verification;

namespace MagicPAI.Workflows;

/// <summary>
/// Simple agent workflow: SpawnContainer -> RunCliAgent -> RunVerification -> DestroyContainer.
/// Suitable for single-task prompts that don't require triage or decomposition.
/// </summary>
public class SimpleAgentWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        builder.Name = "Simple Agent";
        builder.Description = "Execute a single AI agent task with verification";

        // Workflow-level variables (populated from dispatch input)
        var prompt = builder.WithVariable<string>("Prompt", "");
        var workspacePath = builder.WithVariable<string>("WorkspacePath", "/workspace");
        var agent = builder.WithVariable<string>("Agent", "claude");
        var model = builder.WithVariable<string>("Model", "sonnet");
        var containerId = builder.WithVariable<string>("ContainerId", "");

        // Define activities
        var spawn = new SpawnContainerActivity
        {
            WorkspacePath = new Input<string>(workspacePath),
            Id = "spawn-container"
        };

        var runAgent = new RunCliAgentActivity
        {
            Agent = new Input<string>(agent),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Model = new Input<string>(model),
            Id = "run-agent"
        };

        var verify = new RunVerificationActivity
        {
            ContainerId = new Input<string>(containerId),
            Id = "run-verification"
        };

        var destroy = new DestroyContainerActivity
        {
            ContainerId = new Input<string>(containerId),
            Id = "destroy-container"
        };

        // Build flowchart
        var flowchart = new Flowchart
        {
            Id = "simple-agent-flow",
            Start = spawn,
            Connections =
            {
                // SpawnContainer -> RunCliAgent (on Done)
                new Connection(
                    new Endpoint(spawn, "Done"),
                    new Endpoint(runAgent)),

                // RunCliAgent -> RunVerification (on Done)
                new Connection(
                    new Endpoint(runAgent, "Done"),
                    new Endpoint(verify)),

                // RunCliAgent -> DestroyContainer (on Failed)
                new Connection(
                    new Endpoint(runAgent, "Failed"),
                    new Endpoint(destroy)),

                // RunVerification -> DestroyContainer (on Passed)
                new Connection(
                    new Endpoint(verify, "Passed"),
                    new Endpoint(destroy)),

                // RunVerification -> DestroyContainer (on Failed)
                new Connection(
                    new Endpoint(verify, "Failed"),
                    new Endpoint(destroy)),

                // RunVerification -> DestroyContainer (on Inconclusive)
                new Connection(
                    new Endpoint(verify, "Inconclusive"),
                    new Endpoint(destroy)),

                // SpawnContainer -> DestroyContainer (on Failed)
                new Connection(
                    new Endpoint(spawn, "Failed"),
                    new Endpoint(destroy)),
            }
        };

        builder.Root = flowchart;
    }
}
