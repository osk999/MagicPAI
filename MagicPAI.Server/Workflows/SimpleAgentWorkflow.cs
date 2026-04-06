using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Activities;
using Elsa.Workflows.Activities.Flowchart.Models;
using Elsa.Workflows.Models;
using MagicPAI.Activities.AI;
using MagicPAI.Activities.Docker;
using MagicPAI.Activities.Verification;

namespace MagicPAI.Server.Workflows;

/// <summary>
/// Simple agent workflow: SpawnContainer -> RunCliAgent -> RunVerification -> DestroyContainer.
/// </summary>
public class SimpleAgentWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        builder.Name = "Simple Agent";
        builder.Description = "Execute a single AI agent task with verification";

        var prompt = builder.WithVariable<string>("Prompt", "");
        var workspacePath = builder.WithVariable<string>("WorkspacePath", "/workspace");
        var agent = builder.WithVariable<string>("Agent", "claude");
        var model = builder.WithVariable<string>("Model", "sonnet");
        var containerId = builder.WithVariable<string>("ContainerId", "");

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

        var flowchart = new Flowchart
        {
            Id = "simple-agent-flow",
            Start = spawn,
            Connections =
            {
                new Connection(new Endpoint(spawn, "Done"), new Endpoint(runAgent)),
                new Connection(new Endpoint(spawn, "Failed"), new Endpoint(destroy)),
                new Connection(new Endpoint(runAgent, "Done"), new Endpoint(verify)),
                new Connection(new Endpoint(runAgent, "Failed"), new Endpoint(destroy)),
                new Connection(new Endpoint(verify, "Passed"), new Endpoint(destroy)),
                new Connection(new Endpoint(verify, "Failed"), new Endpoint(destroy)),
                new Connection(new Endpoint(verify, "Inconclusive"), new Endpoint(destroy)),
            }
        };

        builder.Root = flowchart;
    }
}
