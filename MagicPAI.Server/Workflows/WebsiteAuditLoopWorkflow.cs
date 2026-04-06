using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Activities;
using Elsa.Workflows.Activities.Flowchart.Models;
using Elsa.Workflows.Models;
using MagicPAI.Activities.AI;

namespace MagicPAI.Server.Workflows;

/// <summary>
/// Four-phase autonomous website audit:
///   Phase 1: Discovery loop - crawl and map pages
///   Phase 2: Visual audit loop - screenshot and analyze each page
///   Phase 3: Interaction + scroll loop - test forms, buttons, scrolling
///   Phase 4: Opus sweep - deep analysis and final report
/// Each phase uses RunCliAgentActivity in a while-like loop pattern via Flowchart
/// connections that loop back from a classifier to the phase runner.
/// </summary>
public class WebsiteAuditLoopWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        builder.Name = "Website Audit Loop";
        builder.Description =
            "Four-phase autonomous website audit: discovery, visual, interaction, and Opus sweep";

        var prompt = builder.WithVariable<string>("Prompt", "");
        var containerId = builder.WithVariable<string>("ContainerId", "");

        // --- Phase 1: Discovery Loop ---
        var discoveryRunner = new RunCliAgentActivity
        {
            Agent = new Input<string>("claude"),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Model = new Input<string>("haiku"),
            Id = "phase1-discovery-runner"
        };

        var discoveryCheck = new TriageActivity
        {
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Id = "phase1-discovery-check"
        };

        // --- Phase 2: Visual Audit Loop ---
        var visualRunner = new RunCliAgentActivity
        {
            Agent = new Input<string>("claude"),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Model = new Input<string>("sonnet"),
            Id = "phase2-visual-runner"
        };

        var visualCheck = new TriageActivity
        {
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Id = "phase2-visual-check"
        };

        // --- Phase 3: Interaction + Scroll Loop ---
        var interactionRunner = new RunCliAgentActivity
        {
            Agent = new Input<string>("claude"),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Model = new Input<string>("sonnet"),
            Id = "phase3-interaction-runner"
        };

        var interactionCheck = new TriageActivity
        {
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Id = "phase3-interaction-check"
        };

        // --- Phase 4: Opus Sweep ---
        var opusSweep = new RunCliAgentActivity
        {
            Agent = new Input<string>("claude"),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Model = new Input<string>("opus"),
            Id = "phase4-opus-sweep"
        };

        var flowchart = new Flowchart
        {
            Id = "website-audit-loop-flow",
            Start = discoveryRunner,
            Connections =
            {
                // Phase 1: Discovery loop
                new Connection(
                    new Endpoint(discoveryRunner, "Done"),
                    new Endpoint(discoveryCheck)),
                new Connection(
                    new Endpoint(discoveryRunner, "Failed"),
                    new Endpoint(discoveryCheck)),
                // Not done -> loop back
                new Connection(
                    new Endpoint(discoveryCheck, "Complex"),
                    new Endpoint(discoveryRunner)),
                // Done -> Phase 2
                new Connection(
                    new Endpoint(discoveryCheck, "Simple"),
                    new Endpoint(visualRunner)),

                // Phase 2: Visual audit loop
                new Connection(
                    new Endpoint(visualRunner, "Done"),
                    new Endpoint(visualCheck)),
                new Connection(
                    new Endpoint(visualRunner, "Failed"),
                    new Endpoint(visualCheck)),
                // Not done -> loop back
                new Connection(
                    new Endpoint(visualCheck, "Complex"),
                    new Endpoint(visualRunner)),
                // Done -> Phase 3
                new Connection(
                    new Endpoint(visualCheck, "Simple"),
                    new Endpoint(interactionRunner)),

                // Phase 3: Interaction + scroll loop
                new Connection(
                    new Endpoint(interactionRunner, "Done"),
                    new Endpoint(interactionCheck)),
                new Connection(
                    new Endpoint(interactionRunner, "Failed"),
                    new Endpoint(interactionCheck)),
                // Not done -> loop back
                new Connection(
                    new Endpoint(interactionCheck, "Complex"),
                    new Endpoint(interactionRunner)),
                // Done -> Phase 4
                new Connection(
                    new Endpoint(interactionCheck, "Simple"),
                    new Endpoint(opusSweep)),
            }
        };

        builder.Root = flowchart;
    }
}
