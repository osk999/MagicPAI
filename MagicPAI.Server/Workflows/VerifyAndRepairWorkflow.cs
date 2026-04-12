using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Activities;
using Elsa.Workflows.Activities.Flowchart.Models;
using Elsa.Workflows.Models;
using MagicPAI.Activities.AI;
using MagicPAI.Activities.Verification;
using MagicPAI.Server.Workflows.Components;

namespace MagicPAI.Server.Workflows;

/// <summary>
/// Reusable verify-and-repair sub-workflow.
/// Flow: RunVerification -> if failed: Repair -> RunCliAgent -> RunVerification (loop back)
/// Can be invoked as a sub-workflow from FullOrchestrateWorkflow or independently.
/// </summary>
public class VerifyAndRepairWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        builder.Name = "Verify and Repair";
        builder.Description = "Run verification gates and auto-repair on failure";

        // Variables (populated from dispatch input or parent workflow)
        var containerId = builder.WithVariable<string>("ContainerId", "");
        var prompt = builder.WithVariable<string>("Prompt", "");
        var agent = builder.WithVariable<string>("AiAssistant", "claude");
        var model = builder.WithVariable<string>("Model", "auto");
        var modelPower = builder.WithVariable<int>("ModelPower", 0);
        var workerOutput = builder.WithVariable<string>("WorkerOutput", "");
        var failedGates = builder.WithVariable<string[]>("FailedGates", []);
        var gateResultsJson = builder.WithVariable<string>("GateResultsJson", "[]");
        var repairPrompt = builder.WithVariable<string>("RepairPrompt", "");
        var repairAttempts = builder.WithVariable<int>("RepairAttempts", 0);

        var loop = VerifyAndRepairLoop.Create(
            verifyId: "verify",
            repairId: "repair",
            repairAgentId: "repair-agent",
            containerId: new Input<string>(containerId),
            originalPrompt: new Input<string>(prompt),
            assistant: new Input<string>(agent),
            model: new Input<string>(model),
            modelPower: new Input<int>(modelPower),
            workerOutput: new Input<string?>(workerOutput),
            failedGates: new Output<string[]>(failedGates),
            gateResultsJson: new Output<string>(gateResultsJson),
            repairPrompt: new Output<string>(repairPrompt));
        var verify = loop.Verify;
        var repair = loop.Repair;
        var repairAgent = loop.RepairAgent;
        Pos(verify, 400, 50);
        Pos(repair, 400, 220);
        Pos(repairAgent, 400, 390);

        // Build a flowchart for the verify-repair loop
        var flowchart = new Flowchart
        {
            Id = "verify-repair-flow",
            Start = verify,
            Activities = { verify, repair, repairAgent },
            Connections =
            {
            }
        };

        foreach (var connection in loop.InternalConnections)
            flowchart.Connections.Add(connection);

        builder.Root = flowchart.WithAttachedVariables(builder);
    }
}
