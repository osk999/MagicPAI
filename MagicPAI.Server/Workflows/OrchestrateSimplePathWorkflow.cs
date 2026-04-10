using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Activities;
using Elsa.Workflows.Activities.Flowchart.Models;
using Elsa.Workflows.Models;
using MagicPAI.Activities.AI;
using MagicPAI.Activities.Verification;

namespace MagicPAI.Server.Workflows;

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
        var agent = builder.WithVariable<string>("AiAssistant", "claude");
        var model = builder.WithVariable<string>("Model", "auto");
        var modelPower = builder.WithVariable<int>("ModelPower", 0);
        var assembledPrompt = builder.WithVariable<string>("AssembledPrompt", "");

        // Step 1: Assemble prompt with context
        var assemblePrompt = new AiAssistantActivity
        {
            AiAssistant = new Input<string>(agent),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            ModelPower = new Input<int>(3),
            Response = new Output<string>(assembledPrompt),
            Id = "simple-assemble-prompt"
        };

        // Step 2: Execute the main agent
        var runAgent = new AiAssistantActivity
        {
            AiAssistant = new Input<string>(agent),
            Prompt = new Input<string>(assembledPrompt),
            ContainerId = new Input<string>(containerId),
            Model = new Input<string>(model),
            ModelPower = new Input<int>(modelPower),
            Id = "simple-agent"
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
            Activities = { assemblePrompt, runAgent, verify },
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

        builder.Root = flowchart.WithAttachedVariables(builder);
    }
}
