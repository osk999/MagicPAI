using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Activities;
using Elsa.Workflows.Activities.Flowchart.Models;
using Elsa.Workflows.Models;
using MagicPAI.Activities.AI;
using MagicPAI.Activities.Verification;

namespace MagicPAI.Workflows;

/// <summary>
/// Child workflow dispatched per-task by BulkDispatchWorkflows.
/// Executes a single decomposed sub-task with model routing, verification, and repair.
/// Mirror of MagicPAI.Server.Workflows.ComplexTaskWorkerWorkflow.
/// </summary>
public class ComplexTaskWorkerWorkflow : Elsa.Workflows.WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        builder.Name = "Complex Task Worker";
        builder.Description =
            "Execute a single decomposed sub-task with model routing, verification, and repair";

        var containerId = builder.WithVariable<string>("ContainerId", "");
        var agent = builder.WithVariable<string>("AiAssistant", "claude");
        var model = builder.WithVariable<string>("Model", "auto");
        var modelPower = builder.WithVariable<int>("ModelPower", 0);
        var selectedAgent = builder.WithVariable<string>("SelectedAgent", "claude");
        var selectedModel = builder.WithVariable<string>("SelectedModel", "sonnet");

        // Step 1: Model router
        var modelRouter = new ModelRouterActivity
        {
            TaskCategory = new Input<string>("code_gen"),
            Complexity = new Input<int>(8),
            PreferredAgent = new Input<string>(agent),
            SelectedAgent = new Output<string>(selectedAgent),
            SelectedModel = new Output<string>(selectedModel),
            Id = "worker-model-router"
        };

        // Step 2: Execute the sub-task
        var worker = new RunCliAgentActivity
        {
            Agent = new Input<string>(selectedAgent),
            Prompt = new Input<string>(ctx =>
            {
                var item = ctx.GetInput<string>("Item") ?? "";
                var original = ctx.GetInput<string>("OriginalPrompt") ?? "";
                return string.IsNullOrWhiteSpace(original)
                    ? item
                    : $"Sub-task: {item}\n\nFull project context:\n{original}";
            }),
            ContainerId = new Input<string>(ctx =>
                ctx.GetInput<string>("ContainerId")
                ?? ctx.GetVariable<string>("ContainerId")
                ?? ""),
            Model = new Input<string>(selectedModel),
            Id = "task-worker"
        };

        // Step 3: Verify
        var verify = new RunVerificationActivity
        {
            ContainerId = new Input<string>(ctx =>
                ctx.GetInput<string>("ContainerId")
                ?? ctx.GetVariable<string>("ContainerId")
                ?? ""),
            Id = "task-verify"
        };

        // Step 4: Repair on failure
        var repair = new RepairActivity
        {
            ContainerId = new Input<string>(ctx =>
                ctx.GetInput<string>("ContainerId")
                ?? ctx.GetVariable<string>("ContainerId")
                ?? ""),
            FailedGates = new Input<string[]>([]),
            OriginalPrompt = new Input<string>(ctx =>
                ctx.GetInput<string>("Item") ?? ""),
            GateResultsJson = new Input<string>(""),
            Id = "task-repair"
        };

        // Step 5: Repair agent
        var repairAgent = new RunCliAgentActivity
        {
            Agent = new Input<string>(selectedAgent),
            Prompt = new Input<string>(ctx =>
                ctx.GetVariable<string>("RepairPrompt") ?? ""),
            ContainerId = new Input<string>(ctx =>
                ctx.GetInput<string>("ContainerId")
                ?? ctx.GetVariable<string>("ContainerId")
                ?? ""),
            Model = new Input<string>(selectedModel),
            Id = "task-repair-agent"
        };

        var flowchart = new Flowchart
        {
            Id = "complex-task-worker-flow",
            Start = modelRouter,
            Activities = { modelRouter, worker, verify, repair, repairAgent },
            Connections =
            {
                new Connection(
                    new Endpoint(modelRouter, "Done"),
                    new Endpoint(worker)),
                new Connection(
                    new Endpoint(worker, "Done"),
                    new Endpoint(verify)),
                new Connection(
                    new Endpoint(verify, "Failed"),
                    new Endpoint(repair)),
                new Connection(
                    new Endpoint(repair, "Done"),
                    new Endpoint(repairAgent)),
                new Connection(
                    new Endpoint(repairAgent, "Done"),
                    new Endpoint(verify)),
                new Connection(
                    new Endpoint(repairAgent, "Failed"),
                    new Endpoint(verify)),
            }
        };

        builder.Root = flowchart;
    }
}
