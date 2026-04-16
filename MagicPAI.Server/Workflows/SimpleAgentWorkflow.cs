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
        Input<string> resolveAssistant() => new(ctx => ctx.ResolveFirst("", "AiAssistant", "Agent"));
        Input<string> resolveModel() => new(ctx => ctx.Resolve("Model"));

        var spawn = new SpawnContainerActivity
        {
            WorkspacePath = new Input<string>("/workspace"),
            ContainerId = new Output<string>(containerId),
            Id = "spawn-container"
        };
        Pos(spawn, 400, 50);

        var runAgent = new AiAssistantActivity
        {
            AiAssistant = resolveAssistant(),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Model = resolveModel(),
            ModelPower = new Input<int>(modelPower),
            Id = "run-agent"
        };
        Pos(runAgent, 400, 220);

        var verify = new RunVerificationActivity
        {
            ContainerId = new Input<string>(containerId),
            Id = "run-verification"
        };
        Pos(verify, 400, 390);

        // Requirements-coverage classifier: grades the finished work against the
        // original user prompt, item by item. On Incomplete it routes to
        // coverageRepairAgent which re-invokes Claude with the gap prompt, then
        // loops back to coverage. Capped at 30 iterations so runaway gaps can't
        // spin forever.
        var coverage = new RequirementsCoverageActivity
        {
            RunAsynchronously = true,
            OriginalPrompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            MaxIterations = new Input<int>(30),
            ModelPower = new Input<int>(2),
            Id = "requirements-coverage"
        };
        Pos(coverage, 400, 560);

        var coverageRepairAgent = new AiAssistantActivity
        {
            RunAsynchronously = true,
            AiAssistant = resolveAssistant(),
            Prompt = new Input<string>(ctx => ctx.GetVariable<string>("RepairPrompt") ?? ""),
            ContainerId = new Input<string>(containerId),
            Model = resolveModel(),
            ModelPower = new Input<int>(modelPower),
            Id = "coverage-repair-agent"
        };
        Pos(coverageRepairAgent, 600, 560);

        var destroy = new DestroyContainerActivity
        {
            ContainerId = new Input<string>(containerId),
            Id = "destroy-container"
        };
        Pos(destroy, 400, 720);

        var flowchart = new Flowchart
        {
            Id = "simple-agent-flow",
            Start = spawn,
            Activities = { spawn, runAgent, verify, coverage, coverageRepairAgent, destroy },
            Connections =
            {
                new Connection(new Endpoint(spawn, "Done"), new Endpoint(runAgent)),
                new Connection(new Endpoint(spawn, "Failed"), new Endpoint(destroy)),
                new Connection(new Endpoint(runAgent, "Done"), new Endpoint(verify)),
                new Connection(new Endpoint(runAgent, "Failed"), new Endpoint(destroy)),
                new Connection(new Endpoint(verify, "Passed"), new Endpoint(coverage)),
                new Connection(new Endpoint(verify, "Failed"), new Endpoint(coverage)),
                new Connection(new Endpoint(verify, "Inconclusive"), new Endpoint(coverage)),

                // Coverage loop: incomplete -> re-run agent with gap prompt -> re-check.
                new Connection(new Endpoint(coverage, "AllMet"), new Endpoint(destroy)),
                new Connection(new Endpoint(coverage, "Exceeded"), new Endpoint(destroy)),
                new Connection(new Endpoint(coverage, "Incomplete"), new Endpoint(coverageRepairAgent)),
                new Connection(new Endpoint(coverageRepairAgent, "Done"), new Endpoint(coverage)),
                new Connection(new Endpoint(coverageRepairAgent, "Failed"), new Endpoint(coverage)),
            }
        };

        builder.Root = flowchart.WithAttachedVariables(builder);
    }
}
