using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Activities;
using Elsa.Workflows.Activities.Flowchart.Models;
using Elsa.Workflows.Models;
using MagicPAI.Activities.AI;
using MagicPAI.Server.Workflows.Components;

namespace MagicPAI.Server.Workflows;

/// <summary>
/// Child workflow dispatched per-task by BulkDispatchWorkflows.
/// Executes a single decomposed sub-task with model routing, verification, and repair.
/// Receives: Item (task description), ContainerId, AiAssistant, Model, ModelPower, OriginalPrompt.
/// </summary>
public class ComplexTaskWorkerWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        builder.Name = "Complex Task Worker";
        builder.Description =
            "Execute a single decomposed sub-task with model routing, verification, and repair";

        var containerId = builder.WithVariable<string>("ContainerId", "").WithWorkflowStorage();
        var agent = builder.WithVariable<string>("AiAssistant", "claude").WithWorkflowStorage();
        var model = builder.WithVariable<string>("Model", "auto").WithWorkflowStorage();
        var modelPower = builder.WithVariable<int>("ModelPower", 0).WithWorkflowStorage();
        var selectedAgent = builder.WithVariable<string>("SelectedAgent", "claude").WithWorkflowStorage();
        var selectedModel = builder.WithVariable<string>("SelectedModel", "sonnet").WithWorkflowStorage();

        // Resolve task prompt: combine Item (sub-task) with OriginalPrompt (full context)
        // NOTE: Item and OriginalPrompt come from BulkDispatch input dict, no same-named
        // variables declared so GetDispatchInput works fine.
        Input<string> resolveTaskPrompt() => new(ctx =>
        {
            var item = ctx.GetDispatchInput("Item") ?? "";
            var original = ctx.GetDispatchInput("OriginalPrompt") ?? "";
            return string.IsNullOrWhiteSpace(original)
                ? item
                : $"""
                  Sub-task: {item}

                  Full project context:
                  {original}
                  """;
        });

        Input<string> resolveContainerId() => new(ctx => ctx.Resolve("ContainerId"));
        Input<string> resolveAgent() => new(ctx => ctx.ResolveFirst("", "AiAssistant", "Agent"));
        Input<int> resolveModelPower() => new(ctx =>
            ctx.GetDispatchInput<int?>("ModelPower")
            ?? ctx.GetVariable<int?>("ModelPower")
            ?? 0);

        // Step 1: Model router selects best model
        var modelRouter = new ModelRouterActivity
        {
            TaskCategory = new Input<string>("code_gen"),
            Complexity = new Input<int>(8),
            PreferredAgent = resolveAgent(),
            SelectedAgent = new Output<string>(selectedAgent),
            SelectedModel = new Output<string>(selectedModel),
            Id = "worker-model-router"
        };
        Pos(modelRouter, 400, 50);

        // Step 2: Execute the sub-task
        var worker = new AiAssistantActivity
        {
            RunAsynchronously = true,
            AiAssistant = new Input<string>(selectedAgent),
            Prompt = resolveTaskPrompt(),
            ContainerId = resolveContainerId(),
            Model = new Input<string>(selectedModel),
            ModelPower = resolveModelPower(),
            Id = "task-worker"
        };
        Pos(worker, 400, 220);

        // Step 3: Verify and repair loop (reuse existing component)
        var loop = VerifyAndRepairLoop.Create(
            verifyId: "task-verify",
            repairId: "task-repair",
            repairAgentId: "task-repair-agent",
            containerId: resolveContainerId(),
            originalPrompt: resolveTaskPrompt(),
            assistant: new Input<string>(selectedAgent),
            model: new Input<string>(selectedModel),
            modelPower: resolveModelPower());
        Pos(loop.Verify, 400, 390);
        Pos(loop.Repair, 250, 560);
        Pos(loop.RepairAgent, 400, 560);

        var flowchart = new Flowchart
        {
            Id = "complex-task-worker-flow",
            Start = modelRouter,
            Activities =
            {
                modelRouter, worker,
                loop.Verify, loop.Repair, loop.RepairAgent
            },
            Connections =
            {
                // ModelRouter -> Worker
                new Connection(
                    new Endpoint(modelRouter, "Done"),
                    new Endpoint(worker)),

                // Worker -> Verify
                new Connection(
                    new Endpoint(worker, "Done"),
                    new Endpoint(loop.Verify)),

                // Worker failed -> workflow ends (faulted)

                // Verify passed/inconclusive -> workflow completes
                // (no outbound connection = flowchart completes)

                // Verify failed -> repair -> repair-agent -> verify (loop)
                // (handled by VerifyAndRepairLoop internal connections)

                // Repair exceeded -> workflow completes
                // (no outbound connection from Exceeded)
            }
        };

        // Add VerifyAndRepairLoop's internal connections
        foreach (var conn in loop.InternalConnections)
            flowchart.Connections.Add(conn);

        builder.Root = flowchart.WithAttachedVariables(builder);
    }
}
