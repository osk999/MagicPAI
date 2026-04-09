using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Activities;
using Elsa.Workflows.Activities.Flowchart.Models;
using Elsa.Workflows.Models;
using Elsa.Workflows.Runtime.Activities;
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

        var containerId = builder.WithVariable<string>("ContainerId", "");
        var enhancedPrompt = builder.WithVariable<string>("EnhancedPrompt", "");
        var elaboratedPrompt = builder.WithVariable<string>("ElaboratedPrompt", "");
        var gatheredContext = builder.WithVariable<string>("GatheredContext", "");
        var complexWorkerOutput = builder.WithVariable<string>("ComplexWorkerOutput", "");
        Input<string> resolveContainerId() => new(ctx =>
            ctx.GetVariable<string>("ContainerId")
            ?? ctx.GetInput<string>("ContainerId")
            ?? "");

        // --- Setup ---
        var spawn = new SpawnContainerActivity
        {
            WorkspacePath = new Input<string>("/workspace"),
            ContainerId = new Output<string>(containerId),
            Id = "std-spawn"
        };

        // --- Prompt Enhancement Phase ---
        var enhancePrompt = new AiAssistantActivity
        {
            AiAssistant = new Input<string>(""),
            Prompt = new Input<string>(""),
            ContainerId = new Input<string>(containerId),
            ModelPower = new Input<int>(2),
            Response = new Output<string>(enhancedPrompt),
            Id = "std-enhance"
        };

        // --- Elaboration Phase ---
        var elaborate = new AiAssistantActivity
        {
            AiAssistant = new Input<string>(""),
            Prompt = new Input<string>(ctx =>
                string.IsNullOrWhiteSpace(ctx.GetVariable<string>("EnhancedPrompt"))
                    ? ctx.GetInput<string>("Prompt") ?? ""
                    : ctx.GetVariable<string>("EnhancedPrompt") ?? ""),
            ContainerId = new Input<string>(containerId),
            ModelPower = new Input<int>(3),
            Response = new Output<string>(elaboratedPrompt),
            Id = "std-elaborate"
        };

        // --- Context Gathering ---
        var gatherContext = new AiAssistantActivity
        {
            AiAssistant = new Input<string>(""),
            Prompt = new Input<string>(ctx =>
                string.IsNullOrWhiteSpace(ctx.GetVariable<string>("ElaboratedPrompt"))
                    ? ctx.GetInput<string>("Prompt") ?? ""
                    : ctx.GetVariable<string>("ElaboratedPrompt") ?? ""),
            ContainerId = new Input<string>(containerId),
            ModelPower = new Input<int>(3),
            Response = new Output<string>(gatheredContext),
            Id = "std-context"
        };

        // --- Triage ---
        var triage = new TriageActivity
        {
            Prompt = new Input<string>(ctx =>
                string.IsNullOrWhiteSpace(ctx.GetVariable<string>("GatheredContext"))
                    ? ctx.GetInput<string>("Prompt") ?? ""
                    : ctx.GetVariable<string>("GatheredContext") ?? ""),
            ContainerId = new Input<string>(containerId),
            Id = "std-triage"
        };

        // --- Simple Path ---
        var simpleAgent = new AiAssistantActivity
        {
            AiAssistant = new Input<string>(""),
            Prompt = new Input<string>(ctx =>
                string.IsNullOrWhiteSpace(ctx.GetVariable<string>("GatheredContext"))
                    ? ctx.GetInput<string>("Prompt") ?? ""
                    : ctx.GetVariable<string>("GatheredContext") ?? ""),
            ContainerId = new Input<string>(containerId),
            Model = new Input<string>(""),
            ModelPower = new Input<int>(0),
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
            Prompt = new Input<string>(ctx =>
                string.IsNullOrWhiteSpace(ctx.GetVariable<string>("GatheredContext"))
                    ? ctx.GetInput<string>("Prompt") ?? ""
                    : ctx.GetVariable<string>("GatheredContext") ?? ""),
            ContainerId = new Input<string>(containerId),
            Id = "std-architect"
        };

        var complexAgent = new AiAssistantActivity
        {
            AiAssistant = new Input<string>(""),
            Prompt = new Input<string>(ctx =>
                string.IsNullOrWhiteSpace(ctx.GetVariable<string>("GatheredContext"))
                    ? ctx.GetInput<string>("Prompt") ?? ""
                    : ctx.GetVariable<string>("GatheredContext") ?? ""),
            ContainerId = new Input<string>(containerId),
            ModelPower = new Input<int>(1),
            Response = new Output<string>(complexWorkerOutput),
            Id = "std-complex-agent"
        };

        var complexVerifyAndRepair = new DispatchWorkflow
        {
            WorkflowDefinitionId = new Input<string>(nameof(VerifyAndRepairWorkflow)),
            WaitForCompletion = new Input<bool>(true),
            Input = new Input<IDictionary<string, object>?>(ctx => new Dictionary<string, object>
            {
                ["ContainerId"] = ctx.GetVariable<string>("ContainerId") ?? "",
                ["Prompt"] = ctx.GetInput<string>("Prompt") ?? "",
                ["AiAssistant"] = ctx.GetInput<string>("AiAssistant") ?? "",
                ["Agent"] = ctx.GetInput<string>("Agent") ?? "",
                ["Model"] = ctx.GetInput<string>("Model") ?? "auto",
                ["ModelPower"] = ctx.GetInput<int>("ModelPower"),
                ["WorkerOutput"] = ctx.GetVariable<string>("ComplexWorkerOutput") ?? ""
            }),
            Id = "std-complex-verify-repair"
        };

        // --- Cleanup ---
        var destroy = new DestroyContainerActivity
        {
            ContainerId = resolveContainerId(),
            Id = "std-destroy"
        };

        var flowchart = new Flowchart
        {
            Id = "standard-orchestrate-flow",
            Start = spawn,
            Activities = { spawn, enhancePrompt, elaborate, gatherContext, triage, simpleAgent, simpleVerify, architect, complexAgent, complexVerifyAndRepair, destroy },
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
                    new Endpoint(complexVerifyAndRepair)),
                new Connection(
                    new Endpoint(complexAgent, "Failed"),
                    new Endpoint(destroy)),
                new Connection(
                    new Endpoint(complexVerifyAndRepair),
                    new Endpoint(destroy)),
            }
        };

        builder.Root = flowchart.WithAttachedVariables(builder);
    }
}
