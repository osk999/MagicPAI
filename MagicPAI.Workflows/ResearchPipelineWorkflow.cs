using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Activities;
using Elsa.Workflows.Activities.Flowchart.Models;
using Elsa.Workflows.Models;
using MagicPAI.Activities.AI;

namespace MagicPAI.Workflows;

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
        var agent = builder.WithVariable<string>("Agent", "claude");
        var model = builder.WithVariable<string>("Model", "sonnet");

        // Step 1: Enhance prompt
        var enhance = new RunCliAgentActivity
        {
            Agent = new Input<string>("claude"),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Model = new Input<string>("sonnet"),
            Id = "research-enhance"
        };

        // Step 2: Research context gathering
        var research = new RunCliAgentActivity
        {
            Agent = new Input<string>("claude"),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Model = new Input<string>("sonnet"),
            Id = "research-gather"
        };

        // Step 3: Triage for routing
        var triage = new TriageActivity
        {
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Id = "research-triage"
        };

        // Step 4a: Simple execution path
        var simpleAgent = new RunCliAgentActivity
        {
            Agent = new Input<string>(agent),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Model = new Input<string>(model),
            Id = "research-simple-exec"
        };

        // Step 4b: Complex decomposition path
        var architect = new ArchitectActivity
        {
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Id = "research-architect"
        };

        var complexAgent = new RunCliAgentActivity
        {
            Agent = new Input<string>(agent),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Model = new Input<string>("opus"),
            Id = "research-complex-exec"
        };

        var flowchart = new Flowchart
        {
            Id = "research-pipeline-flow",
            Start = enhance,
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

        builder.Root = flowchart;
    }
}
