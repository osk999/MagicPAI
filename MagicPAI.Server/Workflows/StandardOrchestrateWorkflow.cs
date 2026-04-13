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
/// Balanced orchestration with research-first prompt grounding,
/// triage, and complex/simple routing.
/// </summary>
public class StandardOrchestrateWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        builder.Name = "Standard Orchestrate";
        builder.Description =
            "Balanced pipeline: research-first prompt grounding, triage, complex/simple routing, verification";

        var containerId = builder.WithVariable<string>("ContainerId", "");
        var researchedPrompt = builder.WithVariable<string>("ResearchedPrompt", "");
        var recommendedModel = builder.WithVariable<string>("RecommendedModel", "");
        var recommendedModelPower = builder.WithVariable<int>("RecommendedModelPower", 0);
        var architectTasks = builder.WithVariable<string[]>("ArchitectTasks", []);
        var complexWorkerOutput = builder.WithVariable<string>("ComplexWorkerOutput", "");

        // NOTE: ctx.GetInput() is shadowed by same-named variables. Use helpers.
        Input<string> resolveContainerId() => new(ctx => ctx.Resolve("ContainerId"));

        Input<string> resolveBestPrompt() => new(ctx =>
        {
            var researched = ctx.GetVariable<string>("ResearchedPrompt");
            return !string.IsNullOrWhiteSpace(researched) ? researched : ctx.GetDispatchInput("Prompt") ?? "";
        });

        Input<string> resolveRecommendedModel() => new(ctx =>
        {
            var requestedModel = ctx.GetDispatchInput("Model");
            if (!string.IsNullOrWhiteSpace(requestedModel) &&
                !string.Equals(requestedModel, "auto", StringComparison.OrdinalIgnoreCase))
                return requestedModel;

            return ctx.GetVariable<string>("RecommendedModel") ?? requestedModel ?? "";
        });

        Input<int> resolveRecommendedPower() => new(ctx =>
        {
            var requestedModel = ctx.GetDispatchInput("Model");
            if (!string.IsNullOrWhiteSpace(requestedModel) &&
                !string.Equals(requestedModel, "auto", StringComparison.OrdinalIgnoreCase))
                return ctx.GetDispatchInput<int?>("ModelPower") ?? 0;

            return ctx.GetVariable<int?>("RecommendedModelPower") ?? ctx.GetDispatchInput<int?>("ModelPower") ?? 0;
        });

        Input<string> resolveComplexPrompt() => new(ctx =>
        {
            var originalPrompt = resolveBestPrompt().Get(ctx);
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

        var spawn = new SpawnContainerActivity
        {
            WorkspacePath = new Input<string>("/workspace"),
            ContainerId = new Output<string>(containerId),
            Id = "std-spawn"
        };
        Pos(spawn, 400, 50);

        var researchPrompt = new ResearchPromptActivity
        {
            AiAssistant = new Input<string>(""),
            Prompt = new Input<string>(""),
            ContainerId = new Input<string>(containerId),
            ModelPower = new Input<int>(2),
            EnhancedPrompt = new Output<string>(researchedPrompt),
            Id = "std-research-prompt"
        };
        Pos(researchPrompt, 400, 200);

        var triage = new TriageActivity
        {
            Prompt = resolveBestPrompt(),
            ContainerId = new Input<string>(containerId),
            RecommendedModel = new Output<string>(recommendedModel),
            RecommendedModelPower = new Output<int>(recommendedModelPower),
            Id = "std-triage"
        };
        Pos(triage, 400, 370);

        var simpleAgent = new AiAssistantActivity
        {
            AiAssistant = new Input<string>(""),
            Prompt = resolveBestPrompt(),
            ContainerId = new Input<string>(containerId),
            Model = resolveRecommendedModel(),
            ModelPower = resolveRecommendedPower(),
            Id = "std-simple-agent"
        };
        Pos(simpleAgent, 200, 540);

        var simpleVerify = new RunVerificationActivity
        {
            ContainerId = new Input<string>(containerId),
            Id = "std-simple-verify"
        };
        Pos(simpleVerify, 200, 710);

        var architect = new ArchitectActivity
        {
            Prompt = resolveBestPrompt(),
            ContainerId = new Input<string>(containerId),
            TaskListJson = new Output<string[]>(architectTasks),
            Id = "std-architect"
        };
        Pos(architect, 600, 540);

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
        Pos(complexAgent, 600, 710);

        var complexLoop = VerifyAndRepairLoop.Create(
            verifyId: "std-complex-verify",
            repairId: "std-complex-repair",
            repairAgentId: "std-repair-agent",
            containerId: new Input<string>(containerId),
            originalPrompt: new Input<string>(ctx => ctx.GetDispatchInput("Prompt") ?? ""),
            assistant: new Input<string>(ctx => ctx.ResolveFirst("", "AiAssistant", "Agent")),
            model: resolveRecommendedModel(),
            modelPower: resolveRecommendedPower(),
            workerOutput: new Input<string?>(complexWorkerOutput));
        var complexVerify = complexLoop.Verify;
        var complexRepair = complexLoop.Repair;
        var repairAgent = complexLoop.RepairAgent;
        Pos(complexVerify, 600, 880);
        Pos(complexRepair, 500, 1050);
        Pos(repairAgent, 700, 1050);

        var destroy = new DestroyContainerActivity
        {
            ContainerId = resolveContainerId(),
            Id = "std-destroy"
        };
        Pos(destroy, 400, 1220);

        var flowchart = new Flowchart
        {
            Id = "standard-orchestrate-flow",
            Start = spawn,
            Activities = { spawn, researchPrompt, triage, simpleAgent, simpleVerify, architect, complexAgent, complexVerify, complexRepair, repairAgent, destroy },
            Connections =
            {
                new Connection(new Endpoint(spawn, "Done"), new Endpoint(researchPrompt)),
                new Connection(new Endpoint(spawn, "Failed"), new Endpoint(destroy)),

                new Connection(new Endpoint(researchPrompt, "Done"), new Endpoint(triage)),
                new Connection(new Endpoint(researchPrompt, "Failed"), new Endpoint(triage)),

                new Connection(new Endpoint(triage, "Simple"), new Endpoint(simpleAgent)),
                new Connection(new Endpoint(triage, "Complex"), new Endpoint(architect)),

                new Connection(new Endpoint(simpleAgent, "Done"), new Endpoint(simpleVerify)),
                new Connection(new Endpoint(simpleAgent, "Failed"), new Endpoint(destroy)),
                new Connection(new Endpoint(simpleVerify, "Passed"), new Endpoint(destroy)),
                new Connection(new Endpoint(simpleVerify, "Failed"), new Endpoint(destroy)),
                new Connection(new Endpoint(simpleVerify, "Inconclusive"), new Endpoint(destroy)),

                new Connection(new Endpoint(architect, "Done"), new Endpoint(complexAgent)),
                new Connection(new Endpoint(architect, "Failed"), new Endpoint(destroy)),
                new Connection(new Endpoint(complexAgent, "Done"), new Endpoint(complexVerify)),
                new Connection(new Endpoint(complexAgent, "Failed"), new Endpoint(destroy)),
                new Connection(new Endpoint(complexVerify, "Passed"), new Endpoint(destroy)),
                new Connection(new Endpoint(complexVerify, "Inconclusive"), new Endpoint(destroy)),
                new Connection(new Endpoint(complexRepair, "Exceeded"), new Endpoint(destroy)),
            }
        };

        foreach (var connection in complexLoop.InternalConnections)
            flowchart.Connections.Add(connection);

        builder.Root = flowchart.WithAttachedVariables(builder);
    }
}
