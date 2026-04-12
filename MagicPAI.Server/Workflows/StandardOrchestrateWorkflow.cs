using System.Linq;
using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Activities;
using Elsa.Workflows.Activities.Flowchart.Models;
using Elsa.Workflows.Models;
using MagicPAI.Activities.AI;
using MagicPAI.Activities.Docker;
using MagicPAI.Activities.Verification;
using MagicPAI.Server.Workflows.Components;

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
        var recommendedModel = builder.WithVariable<string>("RecommendedModel", "");
        var recommendedModelPower = builder.WithVariable<int>("RecommendedModelPower", 0);
        var architectTasks = builder.WithVariable<string[]>("ArchitectTasks", []);
        var complexWorkerOutput = builder.WithVariable<string>("ComplexWorkerOutput", "");
        Input<string> resolveContainerId() => new(ctx =>
            ctx.GetVariable<string>("ContainerId")
            ?? ctx.GetInput<string>("ContainerId")
            ?? "");
        Input<string> resolveRecommendedModel() => new(ctx =>
        {
            var requestedModel = ctx.GetInput<string>("Model");
            if (!string.IsNullOrWhiteSpace(requestedModel) &&
                !string.Equals(requestedModel, "auto", StringComparison.OrdinalIgnoreCase))
                return requestedModel;

            return ctx.GetVariable<string>("RecommendedModel") ?? requestedModel ?? "";
        });
        Input<int> resolveRecommendedPower() => new(ctx =>
        {
            var requestedModel = ctx.GetInput<string>("Model");
            if (!string.IsNullOrWhiteSpace(requestedModel) &&
                !string.Equals(requestedModel, "auto", StringComparison.OrdinalIgnoreCase))
                return ctx.GetInput<int?>("ModelPower") ?? 0;

            return ctx.GetVariable<int?>("RecommendedModelPower") ?? ctx.GetInput<int?>("ModelPower") ?? 0;
        });
        Input<string> resolveComplexPrompt() => new(ctx =>
        {
            var originalPrompt =
                string.IsNullOrWhiteSpace(ctx.GetVariable<string>("GatheredContext"))
                    ? ctx.GetInput<string>("Prompt") ?? ""
                    : ctx.GetVariable<string>("GatheredContext") ?? "";
            var tasks = ctx.GetVariable<string[]>("ArchitectTasks") ?? [];
            if (tasks.Length == 0)
                return originalPrompt;

            var plan = string.Join(Environment.NewLine, tasks.Select((task, index) => $"{index + 1}. {task}"));
            return $$"""
                Execute the task using the architected sub-task plan below.

                Context-rich prompt:
                {{originalPrompt}}

                Sub-tasks:
                {{plan}}
                """;
        });

        // --- Setup ---
        var spawn = new SpawnContainerActivity
        {
            WorkspacePath = new Input<string>("/workspace"),
            ContainerId = new Output<string>(containerId),
            Id = "std-spawn"
        };
        Pos(spawn, 400, 50);

        // --- Prompt Enhancement Phase ---
        var enhancePrompt = new AiAssistantActivity
        {
            AiAssistant = new Input<string>(""),
            Prompt = new Input<string>(""),
            ContainerId = new Input<string>(containerId),
            ModelPower = new Input<int>(2),
            TrackPromptTransform = new Input<bool>(true),
            PromptTransformLabel = new Input<string>("Prompt Enhancement"),
            Response = new Output<string>(enhancedPrompt),
            Id = "std-enhance"
        };
        Pos(enhancePrompt, 400, 170);

        // --- Elaboration Phase ---
        var elaborate = new AiAssistantActivity
        {
            AiAssistant = new Input<string>(""),
            Prompt = new Input<string>(ctx =>
                string.IsNullOrWhiteSpace(ctx.GetVariable<string>("EnhancedPrompt"))
                    ? ctx.GetInput<string>("Prompt") ?? ""
                    : ctx.GetVariable<string>("EnhancedPrompt") ?? ""),
            ContainerId = new Input<string>(containerId),
            ModelPower = new Input<int>(2),
            Response = new Output<string>(elaboratedPrompt),
            Id = "std-elaborate"
        };
        Pos(elaborate, 400, 290);

        // --- Context Gathering ---
        var gatherContext = new AiAssistantActivity
        {
            AiAssistant = new Input<string>(""),
            Prompt = new Input<string>(ctx =>
                string.IsNullOrWhiteSpace(ctx.GetVariable<string>("ElaboratedPrompt"))
                    ? ctx.GetInput<string>("Prompt") ?? ""
                    : ctx.GetVariable<string>("ElaboratedPrompt") ?? ""),
            ContainerId = new Input<string>(containerId),
            ModelPower = new Input<int>(2),
            Response = new Output<string>(gatheredContext),
            Id = "std-context"
        };
        Pos(gatherContext, 400, 410);

        // --- Triage ---
        var triage = new TriageActivity
        {
            Prompt = new Input<string>(ctx =>
                string.IsNullOrWhiteSpace(ctx.GetVariable<string>("GatheredContext"))
                    ? ctx.GetInput<string>("Prompt") ?? ""
                    : ctx.GetVariable<string>("GatheredContext") ?? ""),
            ContainerId = new Input<string>(containerId),
            RecommendedModel = new Output<string>(recommendedModel),
            RecommendedModelPower = new Output<int>(recommendedModelPower),
            Id = "std-triage"
        };
        Pos(triage, 400, 530);

        // --- Simple Path ---
        var simpleAgent = new AiAssistantActivity
        {
            AiAssistant = new Input<string>(""),
            Prompt = new Input<string>(ctx =>
                string.IsNullOrWhiteSpace(ctx.GetVariable<string>("GatheredContext"))
                    ? ctx.GetInput<string>("Prompt") ?? ""
                    : ctx.GetVariable<string>("GatheredContext") ?? ""),
            ContainerId = new Input<string>(containerId),
            Model = resolveRecommendedModel(),
            ModelPower = resolveRecommendedPower(),
            Id = "std-simple-agent"
        };
        Pos(simpleAgent, 200, 700);

        var simpleVerify = new RunVerificationActivity
        {
            ContainerId = new Input<string>(containerId),
            Id = "std-simple-verify"
        };
        Pos(simpleVerify, 200, 870);

        // --- Complex Path ---
        var architect = new ArchitectActivity
        {
            Prompt = new Input<string>(ctx =>
                string.IsNullOrWhiteSpace(ctx.GetVariable<string>("GatheredContext"))
                    ? ctx.GetInput<string>("Prompt") ?? ""
                    : ctx.GetVariable<string>("GatheredContext") ?? ""),
            ContainerId = new Input<string>(containerId),
            TaskListJson = new Output<string[]>(architectTasks),
            Id = "std-architect"
        };
        Pos(architect, 600, 700);

        var complexAgent = new AiAssistantActivity
        {
            AiAssistant = new Input<string>(""),
            Prompt = resolveComplexPrompt(),
            ContainerId = new Input<string>(containerId),
            Model = resolveRecommendedModel(),
            ModelPower = resolveRecommendedPower(),
            Response = new Output<string>(complexWorkerOutput),
            Id = "std-complex-agent"
        };
        Pos(complexAgent, 600, 870);

        var complexLoop = VerifyAndRepairLoop.Create(
            verifyId: "std-complex-verify",
            repairId: "std-complex-repair",
            repairAgentId: "std-repair-agent",
            containerId: new Input<string>(containerId),
            originalPrompt: new Input<string>(ctx => ctx.GetInput<string>("Prompt") ?? ""),
            assistant: new Input<string>(ctx =>
                ctx.GetInput<string>("AiAssistant")
                ?? ctx.GetInput<string>("Agent")
                ?? ""),
            model: resolveRecommendedModel(),
            modelPower: resolveRecommendedPower(),
            workerOutput: new Input<string?>(complexWorkerOutput));
        var complexVerify = complexLoop.Verify;
        var complexRepair = complexLoop.Repair;
        var repairAgent = complexLoop.RepairAgent;
        Pos(complexVerify, 600, 1040);
        Pos(complexRepair, 500, 1210);
        Pos(repairAgent, 700, 1210);

        // --- Cleanup ---
        var destroy = new DestroyContainerActivity
        {
            ContainerId = resolveContainerId(),
            Id = "std-destroy"
        };
        Pos(destroy, 400, 1380);

        var flowchart = new Flowchart
        {
            Id = "standard-orchestrate-flow",
            Start = spawn,
            Activities = { spawn, enhancePrompt, elaborate, gatherContext, triage, simpleAgent, simpleVerify, architect, complexAgent, complexVerify, complexRepair, repairAgent, destroy },
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
                    new Endpoint(complexRepair, "Exceeded"),
                    new Endpoint(destroy)),
            }
        };

        foreach (var connection in complexLoop.InternalConnections)
            flowchart.Connections.Add(connection);

        builder.Root = flowchart.WithAttachedVariables(builder);
    }
}
