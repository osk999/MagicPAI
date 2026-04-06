using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Activities;
using Elsa.Workflows.Activities.Flowchart.Models;
using Elsa.Workflows.Models;
using MagicPAI.Activities.AI;
using MagicPAI.Activities.Docker;
using MagicPAI.Activities.Verification;

namespace MagicPAI.Server.Workflows;

/// <summary>
/// Balanced orchestration with prompt enhancement, elaboration, context gathering,
/// triage, and complex/simple routing. This is the main production pipeline that
/// combines the best of all sub-workflows into a single end-to-end flow.
/// </summary>
public class StandardOrchestrateWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        builder.Name = "Standard Orchestrate";
        builder.Description =
            "Balanced pipeline: prompt enhancement, context, triage, complex/simple routing, verification";

        var prompt = builder.WithVariable<string>("Prompt", "");
        var workspacePath = builder.WithVariable<string>("WorkspacePath", "/workspace");
        var agent = builder.WithVariable<string>("Agent", "claude");
        var model = builder.WithVariable<string>("Model", "sonnet");
        var containerId = builder.WithVariable<string>("ContainerId", "");

        // --- Setup ---
        var spawn = new SpawnContainerActivity
        {
            WorkspacePath = new Input<string>(workspacePath),
            ContainerId = new Output<string>(containerId),
            Id = "std-spawn"
        };

        // --- Prompt Enhancement Phase ---
        var enhancePrompt = new RunCliAgentActivity
        {
            Agent = new Input<string>("claude"),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Model = new Input<string>("sonnet"),
            Id = "std-enhance"
        };

        // --- Elaboration Phase ---
        var elaborate = new RunCliAgentActivity
        {
            Agent = new Input<string>("claude"),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Model = new Input<string>("haiku"),
            Id = "std-elaborate"
        };

        // --- Context Gathering ---
        var gatherContext = new RunCliAgentActivity
        {
            Agent = new Input<string>("claude"),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Model = new Input<string>("haiku"),
            Id = "std-context"
        };

        // --- Triage ---
        var triage = new TriageActivity
        {
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Id = "std-triage"
        };

        // --- Simple Path ---
        var simpleAgent = new RunCliAgentActivity
        {
            Agent = new Input<string>(agent),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Model = new Input<string>(model),
            Id = "std-simple-agent"
        };

        var simpleVerify = new RunVerificationActivity
        {
            ContainerId = new Input<string>(containerId),
            Id = "std-simple-verify"
        };

        // --- Complex Path ---
        var architect = new ArchitectActivity
        {
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Id = "std-architect"
        };

        var complexAgent = new RunCliAgentActivity
        {
            Agent = new Input<string>(agent),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Model = new Input<string>("opus"),
            Id = "std-complex-agent"
        };

        var complexVerify = new RunVerificationActivity
        {
            ContainerId = new Input<string>(containerId),
            Id = "std-complex-verify"
        };

        var repair = new RepairActivity
        {
            ContainerId = new Input<string>(containerId),
            FailedGates = new Input<string[]>([]),
            OriginalPrompt = new Input<string>(prompt),
            GateResultsJson = new Input<string>(""),
            Id = "std-repair"
        };

        var repairAgent = new RunCliAgentActivity
        {
            Agent = new Input<string>(agent),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Model = new Input<string>(model),
            Id = "std-repair-agent"
        };

        // --- Cleanup ---
        var destroy = new DestroyContainerActivity
        {
            ContainerId = new Input<string>(containerId),
            Id = "std-destroy"
        };

        var flowchart = new Flowchart
        {
            Id = "standard-orchestrate-flow",
            Start = spawn,
            Connections =
            {
                // Spawn -> Enhance
                new Connection(
                    new Endpoint(spawn, "Done"),
                    new Endpoint(enhancePrompt)),
                new Connection(
                    new Endpoint(spawn, "Failed"),
                    new Endpoint(destroy)),

                // Enhance -> Elaborate
                new Connection(
                    new Endpoint(enhancePrompt, "Done"),
                    new Endpoint(elaborate)),
                new Connection(
                    new Endpoint(enhancePrompt, "Failed"),
                    new Endpoint(elaborate)),

                // Elaborate -> Context
                new Connection(
                    new Endpoint(elaborate, "Done"),
                    new Endpoint(gatherContext)),
                new Connection(
                    new Endpoint(elaborate, "Failed"),
                    new Endpoint(gatherContext)),

                // Context -> Triage
                new Connection(
                    new Endpoint(gatherContext, "Done"),
                    new Endpoint(triage)),
                new Connection(
                    new Endpoint(gatherContext, "Failed"),
                    new Endpoint(triage)),

                // Triage -> Simple or Complex
                new Connection(
                    new Endpoint(triage, "Simple"),
                    new Endpoint(simpleAgent)),
                new Connection(
                    new Endpoint(triage, "Complex"),
                    new Endpoint(architect)),

                // --- Simple Path ---
                new Connection(
                    new Endpoint(simpleAgent, "Done"),
                    new Endpoint(simpleVerify)),
                new Connection(
                    new Endpoint(simpleAgent, "Failed"),
                    new Endpoint(destroy)),
                new Connection(
                    new Endpoint(simpleVerify, "Passed"),
                    new Endpoint(destroy)),
                new Connection(
                    new Endpoint(simpleVerify, "Failed"),
                    new Endpoint(destroy)),
                new Connection(
                    new Endpoint(simpleVerify, "Inconclusive"),
                    new Endpoint(destroy)),

                // --- Complex Path ---
                new Connection(
                    new Endpoint(architect, "Done"),
                    new Endpoint(complexAgent)),
                new Connection(
                    new Endpoint(architect, "Failed"),
                    new Endpoint(destroy)),
                new Connection(
                    new Endpoint(complexAgent, "Done"),
                    new Endpoint(complexVerify)),
                new Connection(
                    new Endpoint(complexAgent, "Failed"),
                    new Endpoint(destroy)),
                new Connection(
                    new Endpoint(complexVerify, "Passed"),
                    new Endpoint(destroy)),
                new Connection(
                    new Endpoint(complexVerify, "Inconclusive"),
                    new Endpoint(destroy)),
                new Connection(
                    new Endpoint(complexVerify, "Failed"),
                    new Endpoint(repair)),
                new Connection(
                    new Endpoint(repair, "Done"),
                    new Endpoint(repairAgent)),
                new Connection(
                    new Endpoint(repairAgent, "Done"),
                    new Endpoint(complexVerify)),
                new Connection(
                    new Endpoint(repairAgent, "Failed"),
                    new Endpoint(destroy)),
            }
        };

        builder.Root = flowchart;
    }
}
