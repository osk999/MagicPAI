// MagicPAI.Server/Workflows/Temporal/DeepResearchOrchestrateWorkflow.cs
// Temporal port of the Elsa DeepResearchOrchestrateWorkflow. Two-stage:
// research pipeline (strongest model) → standard orchestration using the
// researched prompt. See temporal.md §H.13.
using Temporalio.Workflows;
using MagicPAI.Activities.Contracts;
using MagicPAI.Activities.Docker;
using MagicPAI.Workflows;
using MagicPAI.Workflows.Contracts;

namespace MagicPAI.Server.Workflows;

/// <summary>
/// Deep-research orchestrator. Spawns a research container, delegates to
/// <see cref="ResearchPipelineWorkflow"/> to do heavy research against it, then
/// hands the researched prompt off to <see cref="StandardOrchestrateWorkflow"/>
/// which spawns its own container for implementation. The research container
/// is destroyed in <c>finally</c>.
/// </summary>
/// <remarks>
/// The two-container pattern (research + implementation) matches the reference
/// §H.13 template. StandardOrchestrate is called with <c>EnableGui=false</c>
/// because the research-and-implement flow is typically a batch operation —
/// the caller can still pipe output via the <see cref="ISessionStreamSink"/>.
/// </remarks>
[Workflow]
public class DeepResearchOrchestrateWorkflow
{
    private string _stage = "initializing";

    [WorkflowQuery]
    public string PipelineStage => _stage;

    [WorkflowRun]
    public async Task<DeepResearchOrchestrateOutput> RunAsync(DeepResearchOrchestrateInput input)
    {
        _stage = "spawning-container";

        var spawnInput = new SpawnContainerInput(
            SessionId: input.SessionId,
            WorkspacePath: input.WorkspacePath,
            EnableGui: input.EnableGui);

        var spawn = await Workflow.ExecuteActivityAsync(
            (DockerActivities a) => a.SpawnAsync(spawnInput),
            ActivityProfiles.Container);

        try
        {
            _stage = "deep-research";

            var researchInput = new ResearchPipelineInput(
                SessionId: input.SessionId,
                Prompt: input.Prompt,
                ContainerId: spawn.ContainerId,
                WorkingDirectory: input.WorkspacePath,
                AiAssistant: input.AiAssistant);

            var research = await Workflow.ExecuteChildWorkflowAsync(
                (ResearchPipelineWorkflow w) => w.RunAsync(researchInput),
                new ChildWorkflowOptions { Id = $"{input.SessionId}-research" });

            _stage = "standard-orchestrate";

            // Fallback to the original prompt when research finds nothing to
            // rewrite — ResearchPipeline can legitimately return an empty
            // ResearchedPrompt (e.g., when no external references are needed),
            // and passing an empty prompt downstream breaks the Claude CLI
            // ("Input must be provided either through stdin or as a prompt
            // argument when using --print").
            var forwardedPrompt = string.IsNullOrWhiteSpace(research.ResearchedPrompt)
                ? input.Prompt
                : research.ResearchedPrompt;

            var orchestrateInput = new StandardOrchestrateInput(
                SessionId: input.SessionId,
                Prompt: forwardedPrompt,
                WorkspacePath: input.WorkspacePath,
                AiAssistant: input.AiAssistant,
                Model: input.Model,
                ModelPower: input.ModelPower,
                EnableGui: false);

            var orchestrate = await Workflow.ExecuteChildWorkflowAsync(
                (StandardOrchestrateWorkflow w) => w.RunAsync(orchestrateInput),
                new ChildWorkflowOptions { Id = $"{input.SessionId}-orchestrate" });

            _stage = "completed";
            return new DeepResearchOrchestrateOutput(
                Response: orchestrate.Response,
                VerificationPassed: orchestrate.VerificationPassed,
                ResearchSummary: research.ResearchContext,
                TotalCostUsd: orchestrate.TotalCostUsd);
        }
        finally
        {
            _stage = "cleanup";
            var destroyInput = new DestroyInput(spawn.ContainerId);
            await Workflow.ExecuteActivityAsync(
                (DockerActivities a) => a.DestroyAsync(destroyInput),
                ActivityProfiles.ContainerCleanup);
        }
    }
}
