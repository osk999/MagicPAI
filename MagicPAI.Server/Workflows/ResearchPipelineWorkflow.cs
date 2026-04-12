using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Activities;
using Elsa.Workflows.Activities.Flowchart.Models;
using Elsa.Workflows.Models;
using MagicPAI.Activities.AI;

namespace MagicPAI.Server.Workflows;

/// <summary>
/// Research-first orchestration pipeline:
/// Prompt enhancement -> research -> triage -> route to simple or complex sub-workflows.
/// Composes prompt enhancement, research gathering, and complexity-based routing.
/// </summary>
public class ResearchPipelineWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        builder.Name = "Research Pipeline";
        builder.Description =
            "Research-first orchestration: enhance prompt, research, triage, then route to execution";

        var prompt = builder.WithVariable<string>("Prompt", "");
        var containerId = builder.WithVariable<string>("ContainerId", "");
        var agent = builder.WithVariable<string>("AiAssistant", "claude");
        var model = builder.WithVariable<string>("Model", "auto");
        var modelPower = builder.WithVariable<int>("ModelPower", 0);
        var enhancedPrompt = builder.WithVariable<string>("EnhancedPrompt", "");
        var researchContext = builder.WithVariable<string>("ResearchContext", "");

        // Step 1: Enhance prompt
        var enhance = new AiAssistantActivity
        {
            AiAssistant = new Input<string>(agent),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            ModelPower = new Input<int>(2),
            TrackPromptTransform = new Input<bool>(true),
            PromptTransformLabel = new Input<string>("Research Prompt Enhancement"),
            Response = new Output<string>(enhancedPrompt),
            Id = "research-enhance"
        };
        Pos(enhance, 400, 50);

        // Step 2: Research context gathering
        var research = new AiAssistantActivity
        {
            AiAssistant = new Input<string>(agent),
            Prompt = new Input<string>(ctx =>
                string.IsNullOrWhiteSpace(ctx.GetVariable<string>("EnhancedPrompt"))
                    ? ctx.GetVariable<string>("Prompt") ?? ""
                    : ctx.GetVariable<string>("EnhancedPrompt") ?? ""),
            ContainerId = new Input<string>(containerId),
            ModelPower = new Input<int>(2),
            Response = new Output<string>(researchContext),
            Id = "research-gather"
        };
        Pos(research, 400, 220);

        // Step 3: Triage for routing
        var triage = new TriageActivity
        {
            Prompt = new Input<string>(ctx =>
                string.IsNullOrWhiteSpace(ctx.GetVariable<string>("ResearchContext"))
                    ? ctx.GetVariable<string>("Prompt") ?? ""
                    : ctx.GetVariable<string>("ResearchContext") ?? ""),
            ContainerId = new Input<string>(containerId),
            Id = "research-triage"
        };
        Pos(triage, 400, 390);

        // Step 4a: Simple execution path
        var simpleAgent = new AiAssistantActivity
        {
            AiAssistant = new Input<string>(agent),
            Prompt = new Input<string>(ctx =>
                string.IsNullOrWhiteSpace(ctx.GetVariable<string>("ResearchContext"))
                    ? ctx.GetVariable<string>("Prompt") ?? ""
                    : ctx.GetVariable<string>("ResearchContext") ?? ""),
            ContainerId = new Input<string>(containerId),
            Model = new Input<string>(model),
            ModelPower = new Input<int>(modelPower),
            Id = "research-simple-exec"
        };
        Pos(simpleAgent, 200, 560);

        // Step 4b: Complex decomposition path
        var architect = new ArchitectActivity
        {
            Prompt = new Input<string>(ctx =>
                string.IsNullOrWhiteSpace(ctx.GetVariable<string>("ResearchContext"))
                    ? ctx.GetVariable<string>("Prompt") ?? ""
                    : ctx.GetVariable<string>("ResearchContext") ?? ""),
            ContainerId = new Input<string>(containerId),
            Id = "research-architect"
        };
        Pos(architect, 600, 560);

        var complexAgent = new AiAssistantActivity
        {
            AiAssistant = new Input<string>(agent),
            Prompt = new Input<string>(ctx =>
                string.IsNullOrWhiteSpace(ctx.GetVariable<string>("ResearchContext"))
                    ? ctx.GetVariable<string>("Prompt") ?? ""
                    : ctx.GetVariable<string>("ResearchContext") ?? ""),
            ContainerId = new Input<string>(containerId),
            ModelPower = new Input<int>(1),
            Id = "research-complex-exec"
        };
        Pos(complexAgent, 600, 730);

        var flowchart = new Flowchart
        {
            Id = "research-pipeline-flow",
            Start = enhance,
            Activities = { enhance, research, triage, simpleAgent, architect, complexAgent },
            Connections =
            {
                // Enhance -> Research
                new Connection(
                    new Endpoint(enhance, "Done"),
                    new Endpoint(research)),
                new Connection(
                    new Endpoint(enhance, "Failed"),
                    new Endpoint(research)),

                // Research -> Triage
                new Connection(
                    new Endpoint(research, "Done"),
                    new Endpoint(triage)),
                new Connection(
                    new Endpoint(research, "Failed"),
                    new Endpoint(triage)),

                // Triage -> Simple path
                new Connection(
                    new Endpoint(triage, "Simple"),
                    new Endpoint(simpleAgent)),

                // Triage -> Complex path
                new Connection(
                    new Endpoint(triage, "Complex"),
                    new Endpoint(architect)),

                // Architect -> Complex agent
                new Connection(
                    new Endpoint(architect, "Done"),
                    new Endpoint(complexAgent)),
            }
        };

        builder.Root = flowchart.WithAttachedVariables(builder);
    }
}
