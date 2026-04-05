using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Activities;
using Elsa.Workflows.Activities.Flowchart.Models;
using Elsa.Workflows.Models;
using MagicPAI.Activities.AI;
using MagicPAI.Activities.Docker;
using MagicPAI.Activities.Verification;

namespace MagicPAI.Workflows;

/// <summary>
/// Full orchestration workflow with triage-based routing.
/// Flow: SpawnContainer -> Triage -> (Simple: RunCliAgent | Complex: Architect -> RunCliAgent -> VerifyRepair) -> DestroyContainer
/// This is the primary workflow for end-to-end AI agent orchestration.
/// </summary>
public class FullOrchestrateWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        builder.Name = "Full Orchestrate";
        builder.Description =
            "Complete AI orchestration: triage, agent execution, verification, and repair";

        // Workflow-level variables (populated from dispatch input)
        var prompt = builder.WithVariable<string>("Prompt", "");
        var workspacePath = builder.WithVariable<string>("WorkspacePath", "/workspace");
        var agent = builder.WithVariable<string>("Agent", "claude");
        var model = builder.WithVariable<string>("Model", "sonnet");
        var containerId = builder.WithVariable<string>("ContainerId", "");

        // --- Define Activities ---

        var spawn = new SpawnContainerActivity
        {
            WorkspacePath = new Input<string>(workspacePath),
            Id = "spawn-container"
        };

        var triage = new TriageActivity
        {
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Id = "triage"
        };

        // Simple path: single agent run
        var simpleAgent = new RunCliAgentActivity
        {
            Agent = new Input<string>(agent),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Model = new Input<string>(model),
            Id = "simple-agent"
        };

        // Simple path: verify
        var simpleVerify = new RunVerificationActivity
        {
            ContainerId = new Input<string>(containerId),
            Id = "simple-verify"
        };

        // Complex path: architect decomposition
        var architect = new ArchitectActivity
        {
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Id = "architect"
        };

        // Complex path: run agent for decomposed tasks
        // TODO: In a full implementation, use ForEach/ParallelForEach to iterate
        // over architect's task list. For now, runs a single agent with full prompt.
        var complexAgent = new RunCliAgentActivity
        {
            Agent = new Input<string>(agent),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Model = new Input<string>("opus"),
            Id = "complex-agent"
        };

        // Complex path: verify
        var complexVerify = new RunVerificationActivity
        {
            ContainerId = new Input<string>(containerId),
            Id = "complex-verify"
        };

        // Complex path: repair on failure
        var repair = new RepairActivity
        {
            ContainerId = new Input<string>(containerId),
            FailedGates = new Input<string[]>([]),
            OriginalPrompt = new Input<string>(prompt),
            GateResultsJson = new Input<string>(""),
            Id = "repair"
        };

        // Complex path: repair agent
        var repairAgent = new RunCliAgentActivity
        {
            Agent = new Input<string>(agent),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Model = new Input<string>(model),
            Id = "repair-agent"
        };

        // Cleanup
        var destroy = new DestroyContainerActivity
        {
            ContainerId = new Input<string>(containerId),
            Id = "destroy-container"
        };

        // --- Build Flowchart ---
        var flowchart = new Flowchart
        {
            Id = "full-orchestrate-flow",
            Start = spawn,
            Connections =
            {
                // Spawn -> Triage (on Done)
                new Connection(
                    new Endpoint(spawn, "Done"),
                    new Endpoint(triage)),

                // Spawn failed -> Destroy
                new Connection(
                    new Endpoint(spawn, "Failed"),
                    new Endpoint(destroy)),

                // Triage -> Simple path (Simple outcome)
                new Connection(
                    new Endpoint(triage, "Simple"),
                    new Endpoint(simpleAgent)),

                // Triage -> Complex path (Complex outcome)
                new Connection(
                    new Endpoint(triage, "Complex"),
                    new Endpoint(architect)),

                // --- Simple path ---
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

                // --- Complex path ---
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

                // Complex verify passed -> destroy
                new Connection(
                    new Endpoint(complexVerify, "Passed"),
                    new Endpoint(destroy)),

                // Complex verify inconclusive -> destroy
                new Connection(
                    new Endpoint(complexVerify, "Inconclusive"),
                    new Endpoint(destroy)),

                // Complex verify failed -> repair
                new Connection(
                    new Endpoint(complexVerify, "Failed"),
                    new Endpoint(repair)),

                // Repair -> RepairAgent
                new Connection(
                    new Endpoint(repair, "Done"),
                    new Endpoint(repairAgent)),

                // RepairAgent -> ComplexVerify (retry loop)
                new Connection(
                    new Endpoint(repairAgent, "Done"),
                    new Endpoint(complexVerify)),

                // RepairAgent failed -> destroy
                new Connection(
                    new Endpoint(repairAgent, "Failed"),
                    new Endpoint(destroy)),
            }
        };

        builder.Root = flowchart;
    }
}
