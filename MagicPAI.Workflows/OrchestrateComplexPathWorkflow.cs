using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Activities.Flowchart.Activities;
using Elsa.Workflows.Activities.Flowchart.Models;
using Elsa.Workflows.Models;
using MagicPAI.Activities.AI;
using MagicPAI.Activities.Verification;

namespace MagicPAI.Workflows;

/// <summary>
/// Multi-agent decomposition path:
/// Architect -> route by task count:
///   Single task:  ModelRouter -> Worker -> VerifyRepair -> Merge
///   Multi task:   BulkDispatchWorkflows(ComplexTaskWorkerWorkflow) -> Merge
/// Mirror of MagicPAI.Server.Workflows.OrchestrateComplexPathWorkflow.
/// </summary>
public class OrchestrateComplexPathWorkflow : Elsa.Workflows.WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        builder.Name = "Orchestrate Complex Path";
        builder.Description =
            "Multi-agent decomposition: architect, parallel worker dispatch, verify and repair";

        var prompt = builder.WithVariable<string>("Prompt", "");
        var containerId = builder.WithVariable<string>("ContainerId", "");
        var agent = builder.WithVariable<string>("AiAssistant", "claude");
        var model = builder.WithVariable<string>("Model", "auto");
        var modelPower = builder.WithVariable<int>("ModelPower", 0);
        var selectedAgent = builder.WithVariable<string>("SelectedAgent", "claude");
        var selectedModel = builder.WithVariable<string>("SelectedModel", "sonnet");
        var taskListJson = builder.WithVariable<string[]>("TaskListJson", []);
        var taskCount = builder.WithVariable<int>("TaskCount", 0);
        var completedCount = builder.WithVariable<int>("CompletedWorkers", 0);
        var faultedCount = builder.WithVariable<int>("FaultedWorkers", 0);

        // Step 1: Architect decomposes the task
        var architect = new ArchitectActivity
        {
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            TaskListJson = new Output<string[]>(taskListJson),
            TaskCount = new Output<int>(taskCount),
            Id = "architect-decompose"
        };

        // Step 2: Route by task count
        var taskCountDecision = new FlowDecision(ctx =>
            (ctx.GetVariable<int>("TaskCount")) > 1);
        taskCountDecision.Id = "task-count-decision";

        // ---- Single-task path ----
        var modelRouter = new ModelRouterActivity
        {
            TaskCategory = new Input<string>("code_gen"),
            Complexity = new Input<int>(8),
            PreferredAgent = new Input<string>(agent),
            SelectedAgent = new Output<string>(selectedAgent),
            SelectedModel = new Output<string>(selectedModel),
            Id = "model-router"
        };

        var singleWorker = new RunCliAgentActivity
        {
            Agent = new Input<string>(selectedAgent),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Model = new Input<string>(selectedModel),
            Id = "complex-agent"
        };

        var verify = new RunVerificationActivity
        {
            ContainerId = new Input<string>(containerId),
            Id = "complex-verify"
        };

        var repair = new RepairActivity
        {
            ContainerId = new Input<string>(containerId),
            FailedGates = new Input<string[]>([]),
            OriginalPrompt = new Input<string>(prompt),
            GateResultsJson = new Input<string>(""),
            Id = "complex-repair"
        };

        var repairAgent = new RunCliAgentActivity
        {
            Agent = new Input<string>(selectedAgent),
            Prompt = new Input<string>(ctx =>
                ctx.GetVariable<string>("RepairPrompt") ?? ""),
            ContainerId = new Input<string>(containerId),
            Model = new Input<string>(selectedModel),
            Id = "repair-agent"
        };

        // ---- Multi-task path ----
        var bulkDispatch = new Elsa.Workflows.Runtime.Activities.BulkDispatchWorkflows
        {
            WorkflowDefinitionId = new Input<string>(nameof(ComplexTaskWorkerWorkflow)),
            Items = new Input<object>(ctx =>
            {
                var tasks = ctx.GetVariable<string[]>("TaskListJson");
                return (object)(tasks?.ToList() ?? new List<string>());
            }),
            Input = new Input<IDictionary<string, object>?>(ctx =>
            {
                // Inline Resolve: variable (if non-empty) > dispatch input > fallback
                string R(string name, string fb = "") {
                    var v = ctx.GetVariable<string>(name);
                    if (!string.IsNullOrWhiteSpace(v)) return v;
                    var wf = ctx.GetWorkflowExecutionContext().Input;
                    return wf.TryGetValue(name, out var i) ? i?.ToString() ?? fb : fb;
                }
                return new Dictionary<string, object>
                {
                    ["ContainerId"] = R("ContainerId"),
                    ["AiAssistant"] = R("AiAssistant", R("Agent")),
                    ["Model"] = R("Model", "auto"),
                    ["ModelPower"] = ctx.GetVariable<int?>("ModelPower") ?? 0,
                    ["OriginalPrompt"] = R("Prompt")
                };
            }),
            WaitForCompletion = new Input<bool>(true),
            ChildCompleted = new SetVariable
            {
                Variable = completedCount,
                Value = new Input<object?>(ctx => (object)(completedCount.Get(ctx) + 1))
            },
            ChildFaulted = new SetVariable
            {
                Variable = faultedCount,
                Value = new Input<object?>(ctx => (object)(faultedCount.Get(ctx) + 1))
            },
            Id = "dispatch-workers"
        };

        // ---- Merge (shared by both paths) ----
        var merge = new RunCliAgentActivity
        {
            Agent = new Input<string>(agent),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Model = new Input<string>("sonnet"),
            Id = "merge-results"
        };

        var flowchart = new Flowchart
        {
            Id = "orchestrate-complex-path-flow",
            Start = architect,
            Activities =
            {
                architect, taskCountDecision,
                modelRouter, singleWorker, verify, repair, repairAgent,
                bulkDispatch, merge
            },
            Connections =
            {
                // Architect -> TaskCount decision
                new Connection(
                    new Endpoint(architect, "Done"),
                    new Endpoint(taskCountDecision)),

                // ---- Single-task path (False) ----
                new Connection(
                    new Endpoint(taskCountDecision, "False"),
                    new Endpoint(modelRouter)),
                new Connection(
                    new Endpoint(modelRouter, "Done"),
                    new Endpoint(singleWorker)),
                new Connection(
                    new Endpoint(singleWorker, "Done"),
                    new Endpoint(verify)),
                new Connection(
                    new Endpoint(singleWorker, "Failed"),
                    new Endpoint(merge)),
                new Connection(
                    new Endpoint(verify, "Passed"),
                    new Endpoint(merge)),
                new Connection(
                    new Endpoint(verify, "Inconclusive"),
                    new Endpoint(merge)),
                new Connection(
                    new Endpoint(verify, "Failed"),
                    new Endpoint(repair)),
                new Connection(
                    new Endpoint(repair, "Done"),
                    new Endpoint(repairAgent)),
                new Connection(
                    new Endpoint(repair, "Exceeded"),
                    new Endpoint(merge)),
                new Connection(
                    new Endpoint(repairAgent, "Done"),
                    new Endpoint(verify)),
                new Connection(
                    new Endpoint(repairAgent, "Failed"),
                    new Endpoint(merge)),

                // ---- Multi-task path (True) ----
                new Connection(
                    new Endpoint(taskCountDecision, "True"),
                    new Endpoint(bulkDispatch)),
                new Connection(
                    new Endpoint(bulkDispatch, "Completed"),
                    new Endpoint(merge)),
                new Connection(
                    new Endpoint(bulkDispatch, "Done"),
                    new Endpoint(merge)),
            }
        };

        builder.Root = flowchart;
    }
}
