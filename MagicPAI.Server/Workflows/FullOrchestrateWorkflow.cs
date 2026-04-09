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

        var prompt = builder.WithVariable<string>("Prompt", "");
        var containerId = builder.WithVariable<string>("ContainerId", "");
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
        Input<string> resolveContainerId() => new(ctx =>
            ctx.GetVariable<string>("ContainerId")
            ?? ctx.GetInput<string>("ContainerId")
            ?? "");

        // --- Define Activities ---

        var spawn = new SpawnContainerActivity
        {
            WorkspacePath = new Input<string>(""),
            ContainerId = new Output<string>(containerId),
            Id = "spawn-container"
        };

        var triage = new TriageActivity
        {
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Id = "triage"
        };

        // Simple path: single agent run
        var simpleAgent = new AiAssistantActivity
        {
            AiAssistant = resolveAssistant(),
            Agent = resolveAssistant(),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Model = resolveModel(),
            ModelPower = new Input<int>(modelPower),
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
        var complexAgent = new AiAssistantActivity
        {
            AiAssistant = resolveAssistant(),
            Agent = resolveAssistant(),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Model = resolveModel(),
            ModelPower = new Input<int>(modelPower),
            Id = "complex-agent"
        };

        var complexVerify = new RunVerificationActivity
        {
            ContainerId = new Input<string>(containerId),
            Id = "complex-verify"
        };

        var complexRepair = new RepairActivity
        {
            ContainerId = new Input<string>(containerId),
            OriginalPrompt = new Input<string>(prompt),
            Id = "complex-repair"
        };

        var repairAgent = new AiAssistantActivity
        {
            AiAssistant = resolveAssistant(),
            Agent = resolveAssistant(),
            Prompt = new Input<string>(ctx =>
                ctx.GetVariable<string>("RepairPrompt")
                ?? ctx.GetInput<string>("RepairPrompt")
                ?? ctx.GetVariable<string>("Prompt")
                ?? ctx.GetInput<string>("Prompt")
                ?? ""),
            ContainerId = new Input<string>(containerId),
            Model = resolveModel(),
            ModelPower = new Input<int>(modelPower),
            Id = "repair-agent"
        };

        // Cleanup
        var destroy = new DestroyContainerActivity
        {
            ContainerId = resolveContainerId(),
            Id = "destroy-container"
        };

        // --- Build Flowchart ---
        var flowchart = new Flowchart
        {
            Id = "full-orchestrate-flow",
            Start = spawn,
            Activities = { spawn, triage, simpleAgent, simpleVerify, architect, complexAgent, complexVerify, complexRepair, repairAgent, destroy },
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

                new Connection(
                    new Endpoint(complexVerify, "Passed"),
                    new Endpoint(destroy)),

                new Connection(
                    new Endpoint(complexVerify, "Inconclusive"),
                    new Endpoint(destroy)),

                new Connection(
                    new Endpoint(complexVerify, "Failed"),
                    new Endpoint(complexRepair)),

                new Connection(
                    new Endpoint(complexRepair, "Done"),
                    new Endpoint(repairAgent)),

                new Connection(
                    new Endpoint(complexRepair, "Exceeded"),
                    new Endpoint(destroy)),

                new Connection(
                    new Endpoint(repairAgent, "Done"),
                    new Endpoint(complexVerify)),

                new Connection(
                    new Endpoint(repairAgent, "Failed"),
                    new Endpoint(complexVerify)),
            }
        };

        builder.Root = flowchart.WithAttachedVariables(builder);
    }
}
