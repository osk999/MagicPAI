using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Activities;
using Elsa.Workflows.Activities.Flowchart.Models;
using Elsa.Workflows.Models;
using MagicPAI.Activities.AI;
using MagicPAI.Activities.Verification;

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
        var agent = builder.WithVariable<string>("Agent", "claude");
        var model = builder.WithVariable<string>("Model", "sonnet");
        var workerOutput = builder.WithVariable<string>("WorkerOutput", "");

        // Activities
        var initialVerify = new RunVerificationActivity
        {
            ContainerId = new Input<string>(containerId),
            WorkerOutput = new Input<string?>(workerOutput),
            Id = "initial-verify"
        };

        var repair = new RepairActivity
        {
            ContainerId = new Input<string>(containerId),
            FailedGates = new Input<string[]>([]),
            OriginalPrompt = new Input<string>(prompt),
            GateResultsJson = new Input<string>(""),
            Id = "repair"
        };

        var repairAgent = new RunCliAgentActivity
        {
            Agent = new Input<string>(agent),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Model = new Input<string>(model),
            Id = "repair-agent"
        };

        var retryVerify = new RunVerificationActivity
        {
            ContainerId = new Input<string>(containerId),
            Id = "retry-verify"
        };

        // Build a flowchart for the verify-repair loop
        var flowchart = new Flowchart
        {
            Id = "verify-repair-flow",
            Start = initialVerify,
            Connections =
            {
                // Initial verify fails -> repair
                new Connection(
                    new Endpoint(initialVerify, "Failed"),
                    new Endpoint(repair)),

                // Repair -> RepairAgent
                new Connection(
                    new Endpoint(repair, "Done"),
                    new Endpoint(repairAgent)),

                // RepairAgent done -> retry verification
                new Connection(
                    new Endpoint(repairAgent, "Done"),
                    new Endpoint(retryVerify)),

                // RepairAgent failed -> retry verification anyway
                new Connection(
                    new Endpoint(repairAgent, "Failed"),
                    new Endpoint(retryVerify)),

                // Retry verify failed -> loop back to repair
                new Connection(
                    new Endpoint(retryVerify, "Failed"),
                    new Endpoint(repair)),
            }
        };

        builder.Root = flowchart;
    }
}
