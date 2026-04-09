using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Activities;
using Elsa.Workflows.Activities.Flowchart.Models;
using Elsa.Workflows.Models;
using MagicPAI.Activities.AI;
using MagicPAI.Activities.Verification;

namespace MagicPAI.Server.Workflows;

/// <summary>
/// Multi-agent decomposition path:
/// Architect -> ModelRouter -> parallel workers -> verify -> repair loop -> merge.
/// Used when triage determines a task is too complex for a single agent.
/// </summary>
public class OrchestrateComplexPathWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        builder.Name = "Orchestrate Complex Path";
        builder.Description =
            "Multi-agent decomposition: architect, model routing, parallel workers, verify and repair";

        var prompt = builder.WithVariable<string>("Prompt", "");
        var containerId = builder.WithVariable<string>("ContainerId", "");
        var agent = builder.WithVariable<string>("AiAssistant", "claude");
        var model = builder.WithVariable<string>("Model", "auto");
        var modelPower = builder.WithVariable<int>("ModelPower", 0);
        var selectedAgent = builder.WithVariable<string>("SelectedAgent", "claude");
        var selectedModel = builder.WithVariable<string>("SelectedModel", "sonnet");
        var failedGates = builder.WithVariable<string[]>("FailedGates", []);
        var gateResultsJson = builder.WithVariable<string>("GateResultsJson", "[]");
        var repairPrompt = builder.WithVariable<string>("RepairPrompt", "");
        var repairAttempts = builder.WithVariable<int>("RepairAttempts", 0);

        // Step 1: Architect decomposes the task
        var architect = new ArchitectActivity
        {
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Id = "architect-decompose"
        };

        // Step 2: Model router selects best model for the task
        var modelRouter = new ModelRouterActivity
        {
            TaskCategory = new Input<string>("code_gen"),
            Complexity = new Input<int>(8),
            PreferredAgent = new Input<string>(agent),
            SelectedAgent = new Output<string>(selectedAgent),
            SelectedModel = new Output<string>(selectedModel),
            Id = "model-router"
        };

        // Step 3: Execute worker agent (in production, ForEach over task list)
        var worker = new AiAssistantActivity
        {
            AiAssistant = new Input<string>(selectedAgent),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Model = new Input<string>(selectedModel),
            Id = "complex-worker"
        };

        // Step 4: Verify results
        var verify = new RunVerificationActivity
        {
            ContainerId = new Input<string>(containerId),
            FailedGates = new Output<string[]>(failedGates),
            GateResultsJson = new Output<string>(gateResultsJson),
            Id = "complex-verify"
        };

        // Step 5: Repair on failure
        var repair = new RepairActivity
        {
            ContainerId = new Input<string>(containerId),
            FailedGates = new Input<string[]>(failedGates),
            OriginalPrompt = new Input<string>(prompt),
            GateResultsJson = new Input<string>(gateResultsJson),
            RepairPrompt = new Output<string>(repairPrompt),
            Id = "complex-repair"
        };

        // Step 6: Repair agent
        var repairAgent = new AiAssistantActivity
        {
            AiAssistant = new Input<string>(selectedAgent),
            Prompt = new Input<string>(repairPrompt),
            ContainerId = new Input<string>(containerId),
            Model = new Input<string>(selectedModel),
            Id = "complex-repair-agent"
        };

        // Step 7: Merge results
        var merge = new AiAssistantActivity
        {
            AiAssistant = new Input<string>(agent),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            ModelPower = new Input<int>(2),
            Id = "merge-results"
        };

        var flowchart = new Flowchart
        {
            Id = "orchestrate-complex-path-flow",
            Start = architect,
            Activities = { architect, modelRouter, worker, verify, repair, repairAgent, merge },
            Connections =
            {
                // Architect -> ModelRouter
                new Connection(
                    new Endpoint(architect, "Done"),
                    new Endpoint(modelRouter)),

                // Architect failed -> terminal
                // (no connection = workflow ends)

                // ModelRouter -> Worker
                new Connection(
                    new Endpoint(modelRouter, "Done"),
                    new Endpoint(worker)),

                // Worker -> Verify
                new Connection(
                    new Endpoint(worker, "Done"),
                    new Endpoint(verify)),

                // Worker failed -> merge with partial results
                new Connection(
                    new Endpoint(worker, "Failed"),
                    new Endpoint(merge)),

                // Verify passed -> merge
                new Connection(
                    new Endpoint(verify, "Passed"),
                    new Endpoint(merge)),

                // Verify inconclusive -> merge
                new Connection(
                    new Endpoint(verify, "Inconclusive"),
                    new Endpoint(merge)),

                // Verify failed -> repair
                new Connection(
                    new Endpoint(verify, "Failed"),
                    new Endpoint(repair)),

                // Repair -> RepairAgent
                new Connection(
                    new Endpoint(repair, "Done"),
                    new Endpoint(repairAgent)),

                // Repair attempts exhausted -> merge partial results
                new Connection(
                    new Endpoint(repair, "Exceeded"),
                    new Endpoint(merge)),

                // RepairAgent -> Verify (loop back)
                new Connection(
                    new Endpoint(repairAgent, "Done"),
                    new Endpoint(verify)),

                // RepairAgent failed -> merge
                new Connection(
                    new Endpoint(repairAgent, "Failed"),
                    new Endpoint(merge)),
            }
        };

        builder.Root = flowchart.WithAttachedVariables(builder);
    }
}
