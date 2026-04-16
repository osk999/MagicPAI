using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Activities;
using Elsa.Workflows.Activities.Flowchart.Models;
using Elsa.Workflows.Models;
using MagicPAI.Activities.AI;
using MagicPAI.Activities.Verification;
using MagicPAI.Server.Workflows.Components;

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

        // Hydrate variables from parent's SharedBlackboard entry when dispatched as a child.
        var initVars = ChildInputLoader.Build();
        Pos(initVars, 400, 10);

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
        Pos(researchPrompt, 400, 80);

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

        // Requirements-coverage classifier: grades completed work against the original
        // user requirements. On Incomplete, it stores a gap prompt in the RepairPrompt
        // variable and routes to coverageRepairAgent, which re-invokes Claude with the
        // focused follow-up; then loops back to coverage for re-verification.
        var coverage = new RequirementsCoverageActivity
        {
            RunAsynchronously = true,
            OriginalPrompt = new Input<string>(ctx => ctx.GetDispatchInput("Prompt") ?? ctx.GetVariable<string>("Prompt") ?? ""),
            ContainerId = new Input<string>(containerId),
            MaxIterations = new Input<int>(30),
            ModelPower = new Input<int>(2),
            Id = "simple-coverage"
        };
        Pos(coverage, 400, 560);

        var coverageRepairAgent = new AiAssistantActivity
        {
            RunAsynchronously = true,
            AiAssistant = new Input<string>(agent),
            Prompt = new Input<string>(ctx => ctx.GetVariable<string>("RepairPrompt") ?? ""),
            ContainerId = new Input<string>(containerId),
            Model = new Input<string>(model),
            ModelPower = new Input<int>(modelPower),
            Id = "simple-coverage-repair-agent"
        };
        Pos(coverageRepairAgent, 600, 560);

        var flowchart = new Flowchart
        {
            Id = "orchestrate-simple-path-flow",
            Start = initVars,
            Activities = { initVars, researchPrompt, runAgent, verify, coverage, coverageRepairAgent },
            Connections =
            {
                new Connection(new Endpoint(initVars), new Endpoint(researchPrompt)),
                new Connection(new Endpoint(researchPrompt, "Done"), new Endpoint(runAgent)),
                new Connection(new Endpoint(researchPrompt, "Failed"), new Endpoint(runAgent)),
                new Connection(new Endpoint(runAgent, "Done"), new Endpoint(verify)),
                new Connection(new Endpoint(verify, "Passed"), new Endpoint(coverage)),
                new Connection(new Endpoint(verify, "Failed"), new Endpoint(coverage)),
                new Connection(new Endpoint(verify, "Inconclusive"), new Endpoint(coverage)),

                // Coverage loop: incomplete -> re-run agent with gap prompt -> re-check.
                new Connection(new Endpoint(coverage, "Incomplete"), new Endpoint(coverageRepairAgent)),
                new Connection(new Endpoint(coverageRepairAgent, "Done"), new Endpoint(coverage)),
                new Connection(new Endpoint(coverageRepairAgent, "Failed"), new Endpoint(coverage)),
                // AllMet and Exceeded end the workflow naturally (no outbound connection).
            }
        };

        builder.Root = flowchart.WithAttachedVariables(builder);
    }
}
