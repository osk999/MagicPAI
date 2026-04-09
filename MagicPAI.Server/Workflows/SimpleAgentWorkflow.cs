using Elsa.Extensions;
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

        var containerId = builder.WithVariable<string>("ContainerId", "");
        var prompt = builder.WithVariable<string>("Prompt", "");
        var assistant = builder.WithVariable<string>("AiAssistant", "");
        var model = builder.WithVariable<string>("Model", "");
        var modelPower = builder.WithVariable<int>("ModelPower", 0);
        Input<string> resolveAssistant() => new(ctx =>
            ctx.GetInput<string>("AiAssistant")
            ?? ctx.GetInput<string>("Agent")
            ?? ctx.GetVariable<string>("AiAssistant")
            ?? "");
        Input<string> resolveModel() => new(ctx =>
            ctx.GetInput<string>("Model")
            ?? ctx.GetVariable<string>("Model")
            ?? "");

        var spawn = new SpawnContainerActivity
        {
            WorkspacePath = new Input<string>("/workspace"),
            ContainerId = new Output<string>(containerId),
            Id = "spawn-container"
        };

        var runAgent = new AiAssistantActivity
        {
            AiAssistant = resolveAssistant(),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Model = resolveModel(),
            ModelPower = new Input<int>(modelPower),
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
            Activities = { spawn, runAgent, verify, destroy },
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

        builder.Root = flowchart.WithAttachedVariables(builder);
    }
}
