// MagicPAI.Server/Workflows/Temporal/OrchestrateSimplePathWorkflow.cs
// Temporal port of the Elsa OrchestrateSimplePathWorkflow. Thin wrapper that
// delegates to SimpleAgentWorkflow as a child workflow — exists so future
// pre/post steps (telemetry snapshot, policy gates, etc.) in the simple
// execution path can be added without modifying SimpleAgent itself.
// See temporal.md §H.5.
using Temporalio.Workflows;
using MagicPAI.Workflows.Contracts;

namespace MagicPAI.Server.Workflows;

/// <summary>
/// Dispatches <see cref="SimpleAgentWorkflow"/> as a typed child workflow and
/// projects its output into an <see cref="OrchestrateSimpleOutput"/>. Child
/// workflow id is deterministic: <c>{sessionId}-simple-agent</c>.
/// </summary>
[Workflow]
public class OrchestrateSimplePathWorkflow
{
    [WorkflowRun]
    public async Task<OrchestrateSimpleOutput> RunAsync(OrchestrateSimpleInput input)
    {
        // Forward a non-empty ContainerId as ExistingContainerId so SimpleAgent
        // reuses the caller's container. When this workflow is dispatched
        // top-level via the HTTP API, the controller sends ContainerId="" and
        // SimpleAgent spawns its own container — preserving the standalone
        // contract. When invoked by an orchestrator that already owns a
        // container, ContainerId carries that id through.
        var existingContainerId = string.IsNullOrWhiteSpace(input.ContainerId)
            ? null
            : input.ContainerId;

        // Build child input outside the expression tree (CS9307 compliance).
        var childInput = new SimpleAgentInput(
            SessionId: input.SessionId,
            Prompt: input.Prompt,
            AiAssistant: input.AiAssistant,
            Model: input.Model,
            ModelPower: input.ModelPower,
            WorkspacePath: input.WorkspacePath,
            EnableGui: input.EnableGui,
            ExistingContainerId: existingContainerId);

        var child = await Workflow.ExecuteChildWorkflowAsync(
            (SimpleAgentWorkflow w) => w.RunAsync(childInput),
            new ChildWorkflowOptions { Id = $"{input.SessionId}-simple-agent" });

        return new OrchestrateSimpleOutput(
            Response: child.Response,
            VerificationPassed: child.VerificationPassed,
            TotalCostUsd: child.TotalCostUsd);
    }
}
