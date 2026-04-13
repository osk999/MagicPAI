using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Activities.Flowchart.Activities;
using Elsa.Workflows.Activities.Flowchart.Models;
using Elsa.Workflows.Models;
using MagicPAI.Activities.AI;
using MagicPAI.Server.Workflows.Components;

namespace MagicPAI.Server.Workflows;

/// <summary>
/// Multi-agent decomposition path:
/// Architect -> route by task count:
///   Single task:  ModelRouter -> Worker -> VerifyRepair -> Merge
///   Multi task:   BulkDispatchWorkflows(ComplexTaskWorkerWorkflow) -> Merge
/// </summary>
public class OrchestrateComplexPathWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        builder.Name = "Orchestrate Complex Path";
        builder.Description =
            "Multi-agent decomposition: architect, parallel worker dispatch, verify and repair";

        var prompt = builder.WithVariable<string>("Prompt", "").WithWorkflowStorage();
        var containerId = builder.WithVariable<string>("ContainerId", "").WithWorkflowStorage();
        var agent = builder.WithVariable<string>("AiAssistant", "claude").WithWorkflowStorage();
        var model = builder.WithVariable<string>("Model", "auto").WithWorkflowStorage();
        var modelPower = builder.WithVariable<int>("ModelPower", 0).WithWorkflowStorage();
        var selectedAgent = builder.WithVariable<string>("SelectedAgent", "claude").WithWorkflowStorage();
        var selectedModel = builder.WithVariable<string>("SelectedModel", "sonnet").WithWorkflowStorage();
        var taskListJson = builder.WithVariable<string[]>("TaskListJson", []).WithWorkflowStorage();
        var taskCount = builder.WithVariable<int>("TaskCount", 0).WithWorkflowStorage();
        var completedCount = builder.WithVariable<int>("CompletedWorkers", 0).WithWorkflowStorage();
        var faultedCount = builder.WithVariable<int>("FaultedWorkers", 0).WithWorkflowStorage();

        // ---- Step 0: Initialize variables from dispatch input ----
        // Elsa's ExpressionExecutionContext.GetInput() is shadowed by same-named
        // variables. WorkflowExecutionContext.Input may also be empty for child
        // workflows dispatched via ExecuteWorkflow. The reliable fix: use an Inline
        // activity to copy WorkflowInput to variables at startup.
        var initVars = new Elsa.Workflows.Activities.Inline(ctx =>
        {
            // Elsa child workflows (ExecuteWorkflow/DispatchWorkflow) do NOT reliably
            // propagate parent input to child WorkflowInput. As a workaround, the parent
            // stores child input in SharedBlackboard keyed by child instance ID.
            var bb = ctx.GetRequiredService<MagicPAI.Core.Services.SharedBlackboard>();
            // Parent stores input keyed by its own instance ID + ":child-input".
            var parentId = ctx.WorkflowExecutionContext.Properties.TryGetValue("ParentInstanceId", out var pid) ? pid?.ToString() : null;
            // Also try ParentWorkflowInstanceId
            if (string.IsNullOrWhiteSpace(parentId))
                parentId = ctx.WorkflowExecutionContext.ParentWorkflowInstanceId;
            Console.WriteLine($"[CHILD-INIT-DEBUG] ParentId=\"{parentId}\" Props=[{string.Join(",", ctx.WorkflowExecutionContext.Properties.Keys)}] Input=[{string.Join(",", ctx.WorkflowInput.Keys)}]");
            var stored = !string.IsNullOrWhiteSpace(parentId) ? bb.GetTaskOutput($"{parentId}:child-input") : null;
            Console.WriteLine($"[CHILD-INIT-DEBUG] Stored={stored?.Substring(0, Math.Min(stored?.Length ?? 0, 100))}");

            if (!string.IsNullOrWhiteSpace(stored))
            {
                var data = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(stored);
                if (data is not null)
                {
                    void Set(string name, params string[] keys)
                    {
                        foreach (var key in keys)
                            if (data.TryGetValue(key, out var el) && el.ValueKind == System.Text.Json.JsonValueKind.String)
                            { var s = el.GetString(); if (!string.IsNullOrWhiteSpace(s)) { ctx.SetVariable(name, s); return; } }
                    }
                    Set("Prompt", "Prompt");
                    Set("ContainerId", "ContainerId");
                    Set("AiAssistant", "AiAssistant", "Agent");
                    Set("Model", "Model");
                    if (data.TryGetValue("ModelPower", out var mp) && mp.ValueKind == System.Text.Json.JsonValueKind.Number)
                        ctx.SetVariable("ModelPower", mp.GetInt32());
                }
                if (parentId != null) bb.SetTaskOutput($"{parentId}:child-input", ""); // Clean up
            }
        });
        initVars.Id = "init-vars";
        Pos(initVars, 400, 10);

        // ---- Step 1: Architect decomposes the task ----
        var architect = new ArchitectActivity
        {
            RunAsynchronously = true,
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            TaskListJson = new Output<string[]>(taskListJson),
            TaskCount = new Output<int>(taskCount),
            Id = "architect-decompose"
        };
        Pos(architect, 400, 80);

        // ---- Step 2: Route by task count ----
        var taskCountDecision = new FlowDecision(ctx =>
            (ctx.GetVariable<int>("TaskCount")) > 1);
        taskCountDecision.Id = "task-count-decision";
        Pos(taskCountDecision, 400, 220);

        // ========== SINGLE-TASK PATH (TaskCount <= 1) ==========

        var modelRouter = new ModelRouterActivity
        {
            TaskCategory = new Input<string>("code_gen"),
            Complexity = new Input<int>(8),
            PreferredAgent = new Input<string>(agent),
            SelectedAgent = new Output<string>(selectedAgent),
            SelectedModel = new Output<string>(selectedModel),
            Id = "model-router"
        };
        Pos(modelRouter, 200, 390);

        var singleWorker = new AiAssistantActivity
        {
            RunAsynchronously = true,
            AiAssistant = new Input<string>(selectedAgent),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Model = new Input<string>(selectedModel),
            ModelPower = new Input<int>(modelPower),
            Id = "complex-agent"
        };
        Pos(singleWorker, 200, 560);

        var singleLoop = VerifyAndRepairLoop.Create(
            verifyId: "complex-verify",
            repairId: "complex-repair",
            repairAgentId: "repair-agent",
            containerId: new Input<string>(containerId),
            originalPrompt: new Input<string>(prompt),
            assistant: new Input<string>(selectedAgent),
            model: new Input<string>(selectedModel),
            modelPower: new Input<int>(modelPower));
        Pos(singleLoop.Verify, 200, 730);
        Pos(singleLoop.Repair, 50, 900);
        Pos(singleLoop.RepairAgent, 200, 900);

        // ========== MULTI-TASK PATH (TaskCount > 1) ==========

        var bulkDispatch = new Elsa.Workflows.Runtime.Activities.BulkDispatchWorkflows
        {
            WorkflowDefinitionId = new Input<string>(nameof(ComplexTaskWorkerWorkflow)),
            Items = new Input<object>(ctx =>
            {
                var tasks = ctx.GetVariable<string[]>("TaskListJson");
                return (object)(tasks?.ToList() ?? new List<string>());
            }),
            Input = new Input<IDictionary<string, object>?>(ctx =>
                new Dictionary<string, object>
                {
                    ["ContainerId"] = containerId.Get(ctx),
                    ["AiAssistant"] = agent.Get(ctx),
                    ["Model"] = model.Get(ctx),
                    ["ModelPower"] = modelPower.Get(ctx),
                    ["OriginalPrompt"] = prompt.Get(ctx)
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
        Pos(bulkDispatch, 600, 390);

        // ========== MERGE (shared by both paths) ==========

        var merge = new AiAssistantActivity
        {
            RunAsynchronously = true,
            AiAssistant = new Input<string>(agent),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            ModelPower = new Input<int>(2),
            Id = "merge-results"
        };
        Pos(merge, 400, 1070);

        // ---- Build Flowchart ----
        var flowchart = new Flowchart
        {
            Id = "orchestrate-complex-path-flow",
            Start = initVars,
            Activities =
            {
                initVars,
                architect, taskCountDecision,
                // Single-task path
                modelRouter, singleWorker,
                singleLoop.Verify, singleLoop.Repair, singleLoop.RepairAgent,
                // Multi-task path
                bulkDispatch,
                // Shared
                merge
            },
            Connections =
            {
                // InitVars -> Architect
                new Connection(new Endpoint(initVars), new Endpoint(architect)),

                // Architect -> TaskCount decision
                new Connection(
                    new Endpoint(architect, "Done"),
                    new Endpoint(taskCountDecision)),

                // Architect failed -> workflow ends

                // ---- Single-task path (False = TaskCount <= 1) ----
                new Connection(
                    new Endpoint(taskCountDecision, "False"),
                    new Endpoint(modelRouter)),

                new Connection(
                    new Endpoint(modelRouter, "Done"),
                    new Endpoint(singleWorker)),

                new Connection(
                    new Endpoint(singleWorker, "Done"),
                    new Endpoint(singleLoop.Verify)),

                // Worker failed -> merge with partial results
                new Connection(
                    new Endpoint(singleWorker, "Failed"),
                    new Endpoint(merge)),

                // Verify passed -> merge
                new Connection(
                    new Endpoint(singleLoop.Verify, "Passed"),
                    new Endpoint(merge)),

                // Verify inconclusive -> merge
                new Connection(
                    new Endpoint(singleLoop.Verify, "Inconclusive"),
                    new Endpoint(merge)),

                // Repair exceeded -> merge
                new Connection(
                    new Endpoint(singleLoop.Repair, "Exceeded"),
                    new Endpoint(merge)),

                // ---- Multi-task path (True = TaskCount > 1) ----
                new Connection(
                    new Endpoint(taskCountDecision, "True"),
                    new Endpoint(bulkDispatch)),

                // BulkDispatch completed -> merge
                new Connection(
                    new Endpoint(bulkDispatch, "Completed"),
                    new Endpoint(merge)),
                new Connection(
                    new Endpoint(bulkDispatch, "Done"),
                    new Endpoint(merge)),
            }
        };

        // Add VerifyAndRepairLoop's internal connections (single-task path)
        foreach (var conn in singleLoop.InternalConnections)
            flowchart.Connections.Add(conn);

        builder.Root = flowchart.WithAttachedVariables(builder);
    }
}
