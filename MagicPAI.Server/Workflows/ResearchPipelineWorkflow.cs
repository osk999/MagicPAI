using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Activities;
using Elsa.Workflows.Activities.Flowchart.Models;
using Elsa.Workflows.Models;
using MagicPAI.Activities.AI;

namespace MagicPAI.Server.Workflows;

/// <summary>
/// Research-first orchestration pipeline:
/// run the research component, triage, then route to simple or complex execution.
/// </summary>
public class ResearchPipelineWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        builder.Name = "Research Pipeline";
        builder.Description =
            "Research-first orchestration: ground the prompt, triage it, then route to execution";

        var prompt = builder.WithVariable<string>("Prompt", "");
        var containerId = builder.WithVariable<string>("ContainerId", "");
        var agent = builder.WithVariable<string>("AiAssistant", "claude");
        var model = builder.WithVariable<string>("Model", "auto");
        var modelPower = builder.WithVariable<int>("ModelPower", 0);
        var researchedPrompt = builder.WithVariable<string>("ResearchedPrompt", "");

        Input<string> resolveBestPrompt() => new(ctx =>
            string.IsNullOrWhiteSpace(ctx.GetVariable<string>("ResearchedPrompt"))
                ? ctx.GetVariable<string>("Prompt") ?? ""
                : ctx.GetVariable<string>("ResearchedPrompt") ?? "");

        var researchPrompt = new ResearchPromptActivity
        {
            AiAssistant = new Input<string>(agent),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            ModelPower = new Input<int>(2),
            EnhancedPrompt = new Output<string>(researchedPrompt),
            Id = "research-prompt"
        };
        Pos(researchPrompt, 400, 50);

        var triage = new TriageActivity
        {
            Prompt = resolveBestPrompt(),
            ContainerId = new Input<string>(containerId),
            Id = "research-triage"
        };
        Pos(triage, 400, 220);

        var simpleAgent = new AiAssistantActivity
        {
            AiAssistant = new Input<string>(agent),
            Prompt = resolveBestPrompt(),
            ContainerId = new Input<string>(containerId),
            Model = new Input<string>(model),
            ModelPower = new Input<int>(modelPower),
            Id = "research-simple-exec"
        };
        Pos(simpleAgent, 200, 390);

        var architect = new ArchitectActivity
        {
            Prompt = resolveBestPrompt(),
            ContainerId = new Input<string>(containerId),
            Id = "research-architect"
        };
        Pos(architect, 600, 390);

        var complexAgent = new AiAssistantActivity
        {
            AiAssistant = new Input<string>(agent),
            Prompt = resolveBestPrompt(),
            ContainerId = new Input<string>(containerId),
            ModelPower = new Input<int>(1),
            Id = "research-complex-exec"
        };
        Pos(complexAgent, 600, 560);

        var flowchart = new Flowchart
        {
            Id = "research-pipeline-flow",
            Start = researchPrompt,
            Activities = { researchPrompt, triage, simpleAgent, architect, complexAgent },
            Connections =
            {
                new Connection(new Endpoint(researchPrompt, "Done"), new Endpoint(triage)),
                new Connection(new Endpoint(researchPrompt, "Failed"), new Endpoint(triage)),

                new Connection(new Endpoint(triage, "Simple"), new Endpoint(simpleAgent)),
                new Connection(new Endpoint(triage, "Complex"), new Endpoint(architect)),
                new Connection(new Endpoint(architect, "Done"), new Endpoint(complexAgent)),
            }
        };

        builder.Root = flowchart.WithAttachedVariables(builder);
    }
}
