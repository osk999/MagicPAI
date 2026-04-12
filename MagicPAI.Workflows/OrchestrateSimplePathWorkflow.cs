using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Activities;
using Elsa.Workflows.Activities.Flowchart.Models;
using Elsa.Workflows.Models;
using MagicPAI.Activities.AI;
using MagicPAI.Activities.Verification;

namespace MagicPAI.Workflows;

/// <summary>
/// Single-agent execution path with prompt assembly and context.
/// Used when triage determines a task can be handled by a single agent.
/// Flow: PromptAssembly -> RunCliAgent -> RunVerification
/// </summary>
public class OrchestrateSimplePathWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        builder.Name = "Orchestrate Simple Path";
        builder.Description =
            "Single-agent execution with prompt assembly, context, and verification";

        var prompt = builder.WithVariable<string>("Prompt", "");
        var containerId = builder.WithVariable<string>("ContainerId", "");
        var agent = builder.WithVariable<string>("Agent", "claude");
        var model = builder.WithVariable<string>("Model", "sonnet");

        // Step 1: Assemble prompt with context
        var assemblePrompt = new RunCliAgentActivity
        {
            Agent = new Input<string>("claude"),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Model = new Input<string>("sonnet"),
            Id = "assemble-prompt"
        };

        // Step 2: Execute the main agent
        var runAgent = new RunCliAgentActivity
        {
            Agent = new Input<string>(agent),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Model = new Input<string>(model),
            Id = "simple-execute"
        };

        // Step 3: Verify results
        var verify = new RunVerificationActivity
        {
            ContainerId = new Input<string>(containerId),
            Id = "simple-verify"
        };

        var flowchart = new Flowchart
        {
            Id = "orchestrate-simple-path-flow",
            Start = assemblePrompt,
            Connections =
            {
                // Assemble -> Execute
                new Connection(
                    new Endpoint(assemblePrompt, "Done"),
                    new Endpoint(runAgent)),

                // Assemble failed -> execute with original prompt
                new Connection(
                    new Endpoint(assemblePrompt, "Failed"),
                    new Endpoint(runAgent)),

                // Execute -> Verify
                new Connection(
                    new Endpoint(runAgent, "Done"),
                    new Endpoint(verify)),

                // Execute failed -> terminal
            }
        };

        builder.Root = flowchart;
    }
}
