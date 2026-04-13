using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Models;
using Elsa.Workflows.Models;
using MagicPAI.Activities.AI;
using MagicPAI.Activities.Verification;

namespace MagicPAI.Server.Workflows.Components;

internal sealed record VerifyAndRepairLoopDefinition(
    RunVerificationActivity Verify,
    RepairActivity Repair,
    AiAssistantActivity RepairAgent,
    IReadOnlyList<IActivity> Activities,
    IReadOnlyList<Connection> InternalConnections);

internal static class VerifyAndRepairLoop
{
    public static VerifyAndRepairLoopDefinition Create(
        string verifyId,
        string repairId,
        string repairAgentId,
        Input<string> containerId,
        Input<string> originalPrompt,
        Input<string> assistant,
        Input<string> model,
        Input<int> modelPower,
        Input<string?>? workerOutput = null,
        Output<string[]>? failedGates = null,
        Output<string>? gateResultsJson = null,
        Output<string>? repairPrompt = null)
    {
        var verify = new RunVerificationActivity
        {
            ContainerId = containerId,
            Id = verifyId
        };
        if (workerOutput is not null)
            verify.WorkerOutput = workerOutput;
        if (failedGates is not null)
            verify.FailedGates = failedGates;
        if (gateResultsJson is not null)
            verify.GateResultsJson = gateResultsJson;

        var repair = new RepairActivity
        {
            ContainerId = containerId,
            OriginalPrompt = originalPrompt,
            Id = repairId
        };
        if (repairPrompt is not null)
            repair.RepairPrompt = repairPrompt;

        var repairAgent = new AiAssistantActivity
        {
            AiAssistant = assistant,
            Agent = assistant,
            Prompt = new Input<string>(ctx =>
                ctx.Resolve("RepairPrompt", ctx.Resolve("Prompt"))),
            ContainerId = containerId,
            Model = model,
            ModelPower = modelPower,
            Id = repairAgentId
        };

        var connections = new List<Connection>
        {
            new(
                new Endpoint(verify, "Failed"),
                new Endpoint(repair)),
            new(
                new Endpoint(repair, "Done"),
                new Endpoint(repairAgent)),
            new(
                new Endpoint(repairAgent, "Done"),
                new Endpoint(verify)),
            new(
                new Endpoint(repairAgent, "Failed"),
                new Endpoint(verify))
        };

        return new VerifyAndRepairLoopDefinition(
            verify,
            repair,
            repairAgent,
            [verify, repair, repairAgent],
            connections);
    }
}
