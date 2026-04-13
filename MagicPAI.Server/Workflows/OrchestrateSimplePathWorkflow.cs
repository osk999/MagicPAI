using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Activities;
using Elsa.Workflows.Activities.Flowchart.Models;
using Elsa.Workflows.Models;
using MagicPAI.Activities.AI;
using MagicPAI.Activities.Verification;

namespace MagicPAI.Server.Workflows;

/// <summary>
/// Single-agent execution path with research-first prompt grounding.
/// Used when triage determines a task can be handled by a single agent.
/// Flow: ResearchPrompt -> RunCliAgent -> RunVerification
/// </summary>
public class OrchestrateSimplePathWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        builder.Name = "Orchestrate Simple Path";
        builder.Description =
            "Single-agent execution with research-first prompt grounding and verification";

        var prompt = builder.WithVariable<string>("Prompt", "").WithWorkflowStorage();
        var containerId = builder.WithVariable<string>("ContainerId", "").WithWorkflowStorage();
        var agent = builder.WithVariable<string>("AiAssistant", "claude").WithWorkflowStorage();
        var model = builder.WithVariable<string>("Model", "auto").WithWorkflowStorage();
        var modelPower = builder.WithVariable<int>("ModelPower", 0).WithWorkflowStorage();
        var assembledPrompt = builder.WithVariable<string>("AssembledPrompt", "").WithWorkflowStorage();

        var researchPrompt = new ResearchPromptActivity
        {
            RunAsynchronously = true,
            AiAssistant = new Input<string>(agent),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            ModelPower = new Input<int>(2),
            EnhancedPrompt = new Output<string>(assembledPrompt),
            Id = "simple-research-prompt"
        };
        Pos(researchPrompt, 400, 50);

        var runAgent = new AiAssistantActivity
        {
            RunAsynchronously = true,
            AiAssistant = new Input<string>(agent),
            Prompt = new Input<string>(ctx =>
            {
                var assembled = ctx.GetVariable<string>("AssembledPrompt");
                return !string.IsNullOrWhiteSpace(assembled)
                    ? assembled
                    : ctx.GetDispatchInput("Prompt") ?? "";
            }),
            ContainerId = new Input<string>(containerId),
            Model = new Input<string>(model),
            ModelPower = new Input<int>(modelPower),
            Id = "simple-agent"
        };
        Pos(runAgent, 400, 220);

        var verify = new RunVerificationActivity
        {
            ContainerId = new Input<string>(containerId),
            Id = "simple-verify"
        };
        Pos(verify, 400, 390);

        var flowchart = new Flowchart
        {
            Id = "orchestrate-simple-path-flow",
            Start = researchPrompt,
            Activities = { researchPrompt, runAgent, verify },
            Connections =
            {
                new Connection(new Endpoint(researchPrompt, "Done"), new Endpoint(runAgent)),
                new Connection(new Endpoint(researchPrompt, "Failed"), new Endpoint(runAgent)),
                new Connection(new Endpoint(runAgent, "Done"), new Endpoint(verify)),
            }
        };

        builder.Root = flowchart.WithAttachedVariables(builder);
    }
}
