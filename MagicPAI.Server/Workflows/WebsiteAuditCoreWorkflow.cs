using Elsa.Expressions.Models;
using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Activities;
using Elsa.Workflows.Activities.Flowchart.Models;
using Elsa.Workflows.Models;
using MagicPAI.Activities.AI;
using MagicPAI.Activities.ControlFlow;
using MagicPAI.Server.Workflows.Components;

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
            "Four-phase autonomous website audit core: discovery, audit-and-fix, interaction-and-fix, and final re-verification";
        builder.WithInput<string>("Prompt");
        builder.WithInput<string>("AiAssistant");
        builder.WithInput<string>("Agent");
        builder.WithInput<string>("Model");
        builder.WithInput<int>("ModelPower");
        builder.WithInput<string>("ContainerId");

        var prompt = builder.WithVariable<string>("Prompt", "").WithWorkflowStorage();
        var agent = builder.WithVariable<string>("AiAssistant", "claude").WithWorkflowStorage();
        var containerId = builder.WithVariable<string>("ContainerId", "").WithWorkflowStorage();
        var discoveryOutput = builder.WithVariable<string>("DiscoveryOutput", "").WithWorkflowStorage();
        var visualOutput = builder.WithVariable<string>("VisualOutput", "").WithWorkflowStorage();
        var interactionOutput = builder.WithVariable<string>("InteractionOutput", "").WithWorkflowStorage();
        var discoveryAttempts = builder.WithVariable<int>("DiscoveryAttempts", 0).WithWorkflowStorage();

        // Hydrate variables from parent's SharedBlackboard entry. Without this,
        // ContainerId/Prompt stay empty and every AI activity throws
        // "Container ID is required".
        var initVars = ChildInputLoader.Build();
        Pos(initVars, 400, 10);

        var discoveryGate = new IterationGateActivity
        {
            CurrentCount = new Input<int>(discoveryAttempts),
            NextCount = new Output<int>(discoveryAttempts),
            MaxIterations = new Input<int>(30),
            Label = new Input<string>("Discovery"),
            Id = "phase1-discovery-gate"
        };
        Pos(discoveryGate, 400, 80);

        var discoveryRunner = new AiAssistantActivity
        {
            RunAsynchronously = true,
            AiAssistant = new Input<string>(new Expression("JavaScript", "getInput(\"AiAssistant\") || getInput(\"Agent\") || getVariable(\"AiAssistant\") || \"claude\"")),
            Prompt = new Input<string>(new Expression("JavaScript",
                """
                `Website audit request:
                ${getInput("Prompt") || getVariable("Prompt") || ""}

                Phase 1: Discovery.
                Use Playwright with headed Chromium inside the provided GUI container.
                Map the website, list key pages, identify core user journeys, inspect routes/components when needed, and call out unknown areas that must be covered later.
                This phase is only for discovery and coverage planning, not final sign-off.
                End with DISCOVERY_DONE only when the page map is sufficient for visual review.`
                """)),
            ContainerId = new Input<string>(new Expression("JavaScript", "getInput(\"ContainerId\") || getVariable(\"ContainerId\") || \"\"")),
            ModelPower = new Input<int>(2),
            Response = new Output<string>(discoveryOutput),
            Id = "phase1-discovery-runner"
        };
        Pos(discoveryRunner, 400, 220);

        var discoveryCheck = new TriageActivity
        {
            RunAsynchronously = true,
            Prompt = new Input<string>(new Expression("JavaScript",
                """
                `Original website audit request:
                ${getInput("Prompt") || getVariable("Prompt") || ""}

                Latest discovery report:
                ${getVariable("DiscoveryOutput") || ""}`
                """)),
            ContainerId = new Input<string>(new Expression("JavaScript", "getInput(\"ContainerId\") || getVariable(\"ContainerId\") || \"\"")),
            ClassificationInstructions = new Input<string?>(
                """
                Determine whether website discovery is complete.
                Return low complexity (1-4) when the discovery report is sufficient to move to the visual audit phase.
                Return high complexity (8-10) when additional discovery is still required.
                Set category to "docs".
                """),
            Id = "phase1-discovery-check"
        };
        Pos(discoveryCheck, 400, 390);

        var visualRunner = new AiAssistantActivity
        {
            RunAsynchronously = true,
            AiAssistant = new Input<string>(new Expression("JavaScript", "getInput(\"AiAssistant\") || getInput(\"Agent\") || getVariable(\"AiAssistant\") || \"claude\"")),
            Prompt = new Input<string>(new Expression("JavaScript",
                """
                `Website audit request:
                ${getInput("Prompt") || getVariable("Prompt") || ""}

                Phase 2: Visual audit.
                Use Playwright with headed Chromium inside the provided GUI container.
                Review layout consistency, hierarchy, spacing, responsiveness risks, console/network issues, and visual defects.
                Use the discovery findings below.
                Fix every browser-visible issue you can safely address in the codebase, then run the relevant build/test command and re-check the affected pages in Playwright.
                Do not stop at reporting if a fix is feasible.

                Discovery findings:
                ${getVariable("DiscoveryOutput") || ""}

                End with VISUAL_DONE only when visual review is complete and the audited pages are either fixed or any remaining blockers are explicitly explained.`
                """)),
            ContainerId = new Input<string>(new Expression("JavaScript", "getInput(\"ContainerId\") || getVariable(\"ContainerId\") || \"\"")),
            ModelPower = new Input<int>(2),
            Response = new Output<string>(visualOutput),
            Id = "phase2-visual-runner"
        };
        Pos(visualRunner, 400, 560);

        var visualCheck = new TriageActivity
        {
            RunAsynchronously = true,
            Prompt = new Input<string>(new Expression("JavaScript",
                """
                `Original website audit request:
                ${getInput("Prompt") || getVariable("Prompt") || ""}

                Latest visual audit report:
                ${getVariable("VisualOutput") || ""}`
                """)),
            ContainerId = new Input<string>(new Expression("JavaScript", "getInput(\"ContainerId\") || getVariable(\"ContainerId\") || \"\"")),
            ClassificationInstructions = new Input<string?>(
                """
                Determine whether the visual audit is complete.
                Return low complexity (1-4) when the report is sufficient to proceed to interaction testing.
                Return high complexity (8-10) when another visual pass is needed.
                Set category to "docs".
                """),
            Id = "phase2-visual-check"
        };
        Pos(visualCheck, 400, 730);

        var interactionRunner = new AiAssistantActivity
        {
            RunAsynchronously = true,
            AiAssistant = new Input<string>(new Expression("JavaScript", "getInput(\"AiAssistant\") || getInput(\"Agent\") || getVariable(\"AiAssistant\") || \"claude\"")),
            Prompt = new Input<string>(new Expression("JavaScript",
                """
                `Website audit request:
                ${getInput("Prompt") || getVariable("Prompt") || ""}

                Phase 3: Interaction and scroll audit.
                Use Playwright with headed Chromium inside the provided GUI container.
                Review forms, buttons, navigation flow, focus order, and scrolling behavior.
                Fix interaction issues when feasible, run the relevant build/test command, and re-test in Playwright before concluding the phase.
                Use these prior findings.

                Discovery findings:
                ${getVariable("DiscoveryOutput") || ""}

                Visual findings:
                ${getVariable("VisualOutput") || ""}

                End with INTERACTION_DONE only when interaction coverage is complete and any fixes have been re-tested in the browser.`
                """)),
            ContainerId = new Input<string>(new Expression("JavaScript", "getInput(\"ContainerId\") || getVariable(\"ContainerId\") || \"\"")),
            ModelPower = new Input<int>(2),
            Response = new Output<string>(interactionOutput),
            Id = "phase3-interaction-runner"
        };
        Pos(interactionRunner, 400, 900);

        var interactionCheck = new TriageActivity
        {
            RunAsynchronously = true,
            Prompt = new Input<string>(new Expression("JavaScript",
                """
                `Original website audit request:
                ${getInput("Prompt") || getVariable("Prompt") || ""}

                Latest interaction audit report:
                ${getVariable("InteractionOutput") || ""}`
                """)),
            ContainerId = new Input<string>(new Expression("JavaScript", "getInput(\"ContainerId\") || getVariable(\"ContainerId\") || \"\"")),
            ClassificationInstructions = new Input<string?>(
                """
                Determine whether the interaction audit is complete.
                Return low complexity (1-4) when interaction review is sufficient for the final synthesis pass.
                Return high complexity (8-10) when more interaction testing is required.
                Set category to "testing".
                """),
            Id = "phase3-interaction-check"
        };
        Pos(interactionCheck, 400, 1070);

        var opusSweep = new AiAssistantActivity
        {
            RunAsynchronously = true,
            AiAssistant = new Input<string>(new Expression("JavaScript", "getInput(\"AiAssistant\") || getInput(\"Agent\") || getVariable(\"AiAssistant\") || \"claude\"")),
            Prompt = new Input<string>(new Expression("JavaScript",
                """
                `Website audit request:
                ${getInput("Prompt") || getVariable("Prompt") || ""}

                Phase 4: Final re-verification and synthesis.
                Perform an oops sweep on the highest-risk pages using Playwright.
                Re-check the major visual and interaction fixes, verify there are no newly introduced browser-visible regressions, and fix any newly discovered issue when feasible before concluding.
                Produce a prioritized report with critical issues, user-impact summary, what was fixed, what was re-tested, and any remaining blockers.

                Discovery findings:
                ${getVariable("DiscoveryOutput") || ""}

                Visual findings:
                ${getVariable("VisualOutput") || ""}

                Interaction findings:
                ${getVariable("InteractionOutput") || ""}`
                """)),
            ContainerId = new Input<string>(new Expression("JavaScript", "getInput(\"ContainerId\") || getVariable(\"ContainerId\") || \"\"")),
            ModelPower = new Input<int>(1),
            Id = "phase4-opus-sweep"
        };
        Pos(opusSweep, 400, 1240);

        var flowchart = new Flowchart
        {
            Id = "website-audit-core-flow",
            Start = initVars,
            Activities = { initVars, discoveryGate, discoveryRunner, discoveryCheck, visualRunner, visualCheck, interactionRunner, interactionCheck, opusSweep },
            Connections =
            {
                new Connection(new Endpoint(initVars), new Endpoint(discoveryGate)),
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
