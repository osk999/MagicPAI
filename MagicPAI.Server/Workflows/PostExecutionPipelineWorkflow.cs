// MagicPAI.Server/Workflows/Temporal/PostExecutionPipelineWorkflow.cs
// Temporal port of the Elsa PostExecutionPipelineWorkflow. Runs a final
// verification pass, then uses the cheapest available model (ModelPower=3) to
// generate a Markdown summary report of the session. See temporal.md §H.7.
using Temporalio.Workflows;
using MagicPAI.Activities.AI;
using MagicPAI.Activities.Contracts;
using MagicPAI.Activities.Docker;
using MagicPAI.Activities.Verification;
using MagicPAI.Workflows;
using MagicPAI.Workflows.Contracts;

namespace MagicPAI.Server.Workflows;

/// <summary>
/// Final post-execution pipeline: runs gates one more time then generates a
/// short Markdown session report via the CLI agent. Runs on an existing
/// container — does not own container lifecycle.
/// </summary>
/// <remarks>
/// Container-lifecycle branching mirrors <see cref="SimpleAgentWorkflow"/> (Fix #2).
/// Parent orchestrators pass a non-empty <c>ContainerId</c>; top-level HTTP
/// dispatch sends empty and the workflow spawns its own container (destroyed in
/// <c>finally</c>).
/// </remarks>
[Workflow]
public class PostExecutionPipelineWorkflow
{
    [WorkflowRun]
    public async Task<PostExecOutput> RunAsync(PostExecInput input)
    {
        string containerId;
        bool ownsContainer;
        if (!string.IsNullOrWhiteSpace(input.ContainerId))
        {
            containerId = input.ContainerId;
            ownsContainer = false;
        }
        else
        {
            var spawnInput = new SpawnContainerInput(
                SessionId: input.SessionId,
                WorkspacePath: input.WorkingDirectory,
                EnableGui: false);

            var spawn = await Workflow.ExecuteActivityAsync(
                (DockerActivities a) => a.SpawnAsync(spawnInput),
                ActivityProfiles.Container);

            containerId = spawn.ContainerId;
            ownsContainer = true;
        }

        try
        {
            // Step 1 — final verification pass (compile + test).
            var verifyInput = new VerifyInput(
                ContainerId: containerId,
                WorkingDirectory: input.WorkingDirectory,
                EnabledGates: new[] { "compile", "test" },
                WorkerOutput: input.AgentResponse,
                SessionId: input.SessionId);

            var finalVerify = await Workflow.ExecuteActivityAsync(
                (VerifyActivities a) => a.RunGatesAsync(verifyInput),
                ActivityProfiles.Verify);

            // Step 2 — generate summary report. Use the cheapest model (ModelPower=3)
            // since summarization does not need deep reasoning.
            var reportPrompt = $"""
                Generate a concise Markdown summary of this session's changes.
                Verification: {(finalVerify.AllPassed ? "passed" : "failed")}
                Agent response:
                {input.AgentResponse}
                """;

            var runInput = new RunCliAgentInput(
                Prompt: reportPrompt,
                ContainerId: containerId,
                AiAssistant: input.AiAssistant,
                Model: null,
                ModelPower: 3,
                WorkingDirectory: input.WorkingDirectory,
                SessionId: input.SessionId);

            var report = await Workflow.ExecuteActivityAsync(
                (AiActivities a) => a.RunCliAgentAsync(runInput),
                ActivityProfiles.Medium);

            return new PostExecOutput(
                ReportGenerated: true,
                ReportMarkdown: report.Response,
                CostUsd: report.CostUsd);
        }
        finally
        {
            if (ownsContainer)
            {
                var destroyInput = new DestroyInput(containerId);
                await Workflow.ExecuteActivityAsync(
                    (DockerActivities a) => a.DestroyAsync(destroyInput),
                    ActivityProfiles.ContainerCleanup);
            }
        }
    }
}
