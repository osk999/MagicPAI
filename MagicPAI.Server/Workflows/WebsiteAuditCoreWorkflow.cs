using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Activities;
using Elsa.Workflows.Activities.Flowchart.Models;
using Elsa.Workflows.Models;
using MagicPAI.Activities.AI;
using MagicPAI.Activities.ControlFlow;

namespace MagicPAI.Server.Workflows;

/// <summary>
/// Spawnless website-audit core that assumes a container already exists.
/// This allows full-orchestrate to reuse the audit path without double-spawning.
/// </summary>
public class WebsiteAuditCoreWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        builder.Name = "Website Audit Core";
        builder.Description =
            "Four-phase autonomous website audit core: discovery, visual, interaction, and final synthesis";

        var prompt = builder.WithVariable<string>("Prompt", "");
        var agent = builder.WithVariable<string>("AiAssistant", "claude");
        var containerId = builder.WithVariable<string>("ContainerId", "");
        var discoveryOutput = builder.WithVariable<string>("DiscoveryOutput", "");
        var visualOutput = builder.WithVariable<string>("VisualOutput", "");
        var interactionOutput = builder.WithVariable<string>("InteractionOutput", "");
        var discoveryAttempts = builder.WithVariable<int>("DiscoveryAttempts", 0);

        var discoveryGate = new IterationGateActivity
        {
            CurrentCount = new Input<int>(discoveryAttempts),
            NextCount = new Output<int>(discoveryAttempts),
            MaxIterations = new Input<int>(2),
            Label = new Input<string>("Discovery"),
            Id = "phase1-discovery-gate"
        };

        var discoveryRunner = new AiAssistantActivity
        {
            AiAssistant = new Input<string>(agent),
            Prompt = new Input<string>(ctx =>
                $$"""
                Website audit request:
                {{ctx.GetInput<string>("Prompt") ?? ctx.GetVariable<string>("Prompt") ?? ""}}

                Phase 1: Discovery.
                Use Playwright with headed Chromium inside the provided GUI container.
                Map the website, list key pages, identify core user journeys, and call out unknown areas.
                End with DISCOVERY_DONE only when the page map is sufficient for visual review.
                """),
            ContainerId = new Input<string>(containerId),
            ModelPower = new Input<int>(3),
            Response = new Output<string>(discoveryOutput),
            Id = "phase1-discovery-runner"
        };

        var discoveryCheck = new TriageActivity
        {
            Prompt = new Input<string>(ctx =>
                $$"""
                Original website audit request:
                {{ctx.GetInput<string>("Prompt") ?? ctx.GetVariable<string>("Prompt") ?? ""}}

                Latest discovery report:
                {{ctx.GetVariable<string>("DiscoveryOutput") ?? ""}}
                """),
            ContainerId = new Input<string>(containerId),
            ClassificationInstructions = new Input<string?>(
                """
                Determine whether website discovery is complete.
                Return low complexity (1-4) when the discovery report is sufficient to move to the visual audit phase.
                Return high complexity (8-10) when additional discovery is still required.
                Set category to "docs".
                """),
            Id = "phase1-discovery-check"
        };

        var visualRunner = new AiAssistantActivity
        {
            AiAssistant = new Input<string>(agent),
            Prompt = new Input<string>(ctx =>
                $$"""
                Website audit request:
                {{ctx.GetInput<string>("Prompt") ?? ctx.GetVariable<string>("Prompt") ?? ""}}

                Phase 2: Visual audit.
                Use Playwright with headed Chromium inside the provided GUI container.
                Review layout consistency, hierarchy, spacing, responsiveness risks, and visual defects.
                Use the discovery findings below.

                Discovery findings:
                {{ctx.GetVariable<string>("DiscoveryOutput") ?? ""}}

                End with VISUAL_DONE only when visual review is complete.
                """),
            ContainerId = new Input<string>(containerId),
            ModelPower = new Input<int>(2),
            Response = new Output<string>(visualOutput),
            Id = "phase2-visual-runner"
        };

        var visualCheck = new TriageActivity
        {
            Prompt = new Input<string>(ctx =>
                $$"""
                Original website audit request:
                {{ctx.GetInput<string>("Prompt") ?? ctx.GetVariable<string>("Prompt") ?? ""}}

                Latest visual audit report:
                {{ctx.GetVariable<string>("VisualOutput") ?? ""}}
                """),
            ContainerId = new Input<string>(containerId),
            ClassificationInstructions = new Input<string?>(
                """
                Determine whether the visual audit is complete.
                Return low complexity (1-4) when the report is sufficient to proceed to interaction testing.
                Return high complexity (8-10) when another visual pass is needed.
                Set category to "docs".
                """),
            Id = "phase2-visual-check"
        };

        var interactionRunner = new AiAssistantActivity
        {
            AiAssistant = new Input<string>(agent),
            Prompt = new Input<string>(ctx =>
                $$"""
                Website audit request:
                {{ctx.GetInput<string>("Prompt") ?? ctx.GetVariable<string>("Prompt") ?? ""}}

                Phase 3: Interaction and scroll audit.
                Use Playwright with headed Chromium inside the provided GUI container.
                Review forms, buttons, navigation flow, focus order, and scrolling behavior.
                Use these prior findings.

                Discovery findings:
                {{ctx.GetVariable<string>("DiscoveryOutput") ?? ""}}

                Visual findings:
                {{ctx.GetVariable<string>("VisualOutput") ?? ""}}

                End with INTERACTION_DONE only when interaction coverage is complete.
                """),
            ContainerId = new Input<string>(containerId),
            ModelPower = new Input<int>(2),
            Response = new Output<string>(interactionOutput),
            Id = "phase3-interaction-runner"
        };

        var interactionCheck = new TriageActivity
        {
            Prompt = new Input<string>(ctx =>
                $$"""
                Original website audit request:
                {{ctx.GetInput<string>("Prompt") ?? ctx.GetVariable<string>("Prompt") ?? ""}}

                Latest interaction audit report:
                {{ctx.GetVariable<string>("InteractionOutput") ?? ""}}
                """),
            ContainerId = new Input<string>(containerId),
            ClassificationInstructions = new Input<string?>(
                """
                Determine whether the interaction audit is complete.
                Return low complexity (1-4) when interaction review is sufficient for the final synthesis pass.
                Return high complexity (8-10) when more interaction testing is required.
                Set category to "testing".
                """),
            Id = "phase3-interaction-check"
        };

        var opusSweep = new AiAssistantActivity
        {
            AiAssistant = new Input<string>(agent),
            Prompt = new Input<string>(ctx =>
                $$"""
                Website audit request:
                {{ctx.GetInput<string>("Prompt") ?? ctx.GetVariable<string>("Prompt") ?? ""}}

                Phase 4: Final audit synthesis.
                Produce a prioritized report with critical issues, user-impact summary, and remediation guidance.

                Discovery findings:
                {{ctx.GetVariable<string>("DiscoveryOutput") ?? ""}}

                Visual findings:
                {{ctx.GetVariable<string>("VisualOutput") ?? ""}}

                Interaction findings:
                {{ctx.GetVariable<string>("InteractionOutput") ?? ""}}
                """),
            ContainerId = new Input<string>(containerId),
            ModelPower = new Input<int>(1),
            Id = "phase4-opus-sweep"
        };

        var flowchart = new Flowchart
        {
            Id = "website-audit-core-flow",
            Start = discoveryGate,
            Activities = { discoveryGate, discoveryRunner, discoveryCheck, visualRunner, visualCheck, interactionRunner, interactionCheck, opusSweep },
            Connections =
            {
                new Connection(new Endpoint(discoveryGate, "Continue"), new Endpoint(discoveryRunner)),
                new Connection(new Endpoint(discoveryRunner, "Done"), new Endpoint(discoveryCheck)),
                new Connection(new Endpoint(discoveryRunner, "Failed"), new Endpoint(discoveryCheck)),
                new Connection(new Endpoint(discoveryCheck, "Complex"), new Endpoint(discoveryGate)),
                new Connection(new Endpoint(discoveryCheck, "Simple"), new Endpoint(visualRunner)),

                new Connection(new Endpoint(visualRunner, "Done"), new Endpoint(visualCheck)),
                new Connection(new Endpoint(visualRunner, "Failed"), new Endpoint(visualCheck)),
                new Connection(new Endpoint(visualCheck, "Complex"), new Endpoint(visualRunner)),
                new Connection(new Endpoint(visualCheck, "Simple"), new Endpoint(interactionRunner)),

                new Connection(new Endpoint(interactionRunner, "Done"), new Endpoint(interactionCheck)),
                new Connection(new Endpoint(interactionRunner, "Failed"), new Endpoint(interactionCheck)),
                new Connection(new Endpoint(interactionCheck, "Complex"), new Endpoint(interactionRunner)),
                new Connection(new Endpoint(interactionCheck, "Simple"), new Endpoint(opusSweep)),
            }
        };

        builder.Root = flowchart.WithAttachedVariables(builder);
    }
}
