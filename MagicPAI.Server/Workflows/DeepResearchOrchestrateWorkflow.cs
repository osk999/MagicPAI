using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Activities.Flowchart.Activities;
using Elsa.Workflows.Activities.Flowchart.Models;
using Elsa.Workflows.Models;
using MagicPAI.Activities.AI;
using MagicPAI.Activities.ControlFlow;
using MagicPAI.Activities.Docker;
using MagicPAI.Server.Workflows.Components;

namespace MagicPAI.Server.Workflows;

/// <summary>
/// Deep research orchestration with classifier-verified loops:
///   EnhancePrompt → [Research Loop: research → classifier → loop if missing]
///   → Execute agent → VerifyRepair
///   → [Website Audit Loop: audit → classifier → loop if issues remain]
///   → Done
/// </summary>
public class DeepResearchOrchestrateWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        builder.Name = "Deep Research Orchestrate";
        builder.Description =
            "Classifier-verified research loop + execution + website audit loop";

        // --- Variables ---
        var prompt = builder.WithVariable<string>("Prompt", "").WithWorkflowStorage();
        var containerId = builder.WithVariable<string>("ContainerId", "").WithWorkflowStorage();
        var agent = builder.WithVariable<string>("AiAssistant", "claude").WithWorkflowStorage();
        var model = builder.WithVariable<string>("Model", "auto").WithWorkflowStorage();
        var modelPower = builder.WithVariable<int>("ModelPower", 0).WithWorkflowStorage();
        var enhancedPrompt = builder.WithVariable<string>("EnhancedPrompt", "").WithWorkflowStorage();
        var researchedPrompt = builder.WithVariable<string>("ResearchedPrompt", "").WithWorkflowStorage();
        var researchIterations = builder.WithVariable<int>("ResearchIterations", 0).WithWorkflowStorage();
        var auditIterations = builder.WithVariable<int>("AuditIterations", 0).WithWorkflowStorage();
        var isWebsiteTask = builder.WithVariable<bool>("IsWebsiteTask", false).WithWorkflowStorage();

        // --- Step 0: Initialize variables from dispatch input ---
        var initVars = new Inline(ctx =>
        {
            var wfInput = ctx.WorkflowInput;
            void Set(string name, params string[] keys)
            {
                foreach (var key in keys)
                    if (wfInput.TryGetValue(key, out var val) && val is string s && !string.IsNullOrWhiteSpace(s))
                    { ctx.SetVariable(name, s); return; }
            }
            Set("Prompt", "Prompt");
            Set("AiAssistant", "AiAssistant", "Agent");
            Set("Model", "Model");
            if (wfInput.TryGetValue("ModelPower", out var mp))
            {
                if (mp is int i) ctx.SetVariable("ModelPower", i);
                else if (int.TryParse(mp?.ToString(), out var pi)) ctx.SetVariable("ModelPower", pi);
            }
        });
        initVars.Id = "init-vars";
        Pos(initVars, 400, 10);

        // --- Step 1: Spawn container ---
        var spawn = new SpawnContainerActivity
        {
            WorkspacePath = new Input<string>(""),
            EnableGui = new Input<bool>(true),
            ContainerId = new Output<string>(containerId),
            Id = "spawn-container"
        };
        Pos(spawn, 400, 80);

        // --- Step 2: Enhance prompt (single pass) ---
        var enhance = new PromptEnhancementActivity
        {
            OriginalPrompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            ModelPower = new Input<int>(2),
            EnhancedPrompt = new Output<string>(enhancedPrompt),
            Id = "enhance-prompt"
        };
        Pos(enhance, 400, 180);

        // Update Prompt variable with enhanced version so all downstream activities use it
        var applyEnhanced = new Inline(ctx =>
        {
            var enhanced = ctx.GetVariable<string>("EnhancedPrompt");
            if (!string.IsNullOrWhiteSpace(enhanced))
                ctx.SetVariable("Prompt", enhanced);
        });
        applyEnhanced.Id = "apply-enhanced";
        Pos(applyEnhanced, 400, 230);

        // --- Step 3: Research loop ---
        // 3a: Iteration gate (max 5 research passes)
        var researchGate = new IterationGateActivity
        {
            CurrentCount = new Input<int>(researchIterations),
            NextCount = new Output<int>(researchIterations),
            MaxIterations = new Input<int>(60),
            Label = new Input<string>("Research Loop"),
            Id = "research-gate"
        };
        Pos(researchGate, 400, 280);

        // 3b: Research activity (codebase analysis + repo-map)
        var research = new ResearchPromptActivity
        {
            AiAssistant = new Input<string>(agent),
            Prompt = new Input<string>(prompt), // Uses latest Prompt (updated after enhance)
            ContainerId = new Input<string>(containerId),
            ModelPower = new Input<int>(1),
            EnhancedPrompt = new Output<string>(researchedPrompt),
            Id = "research-pass"
        };
        Pos(research, 400, 380);

        // Update Prompt with research findings so next loop iteration builds on it
        var applyResearch = new Inline(ctx =>
        {
            var researched = ctx.GetVariable<string>("ResearchedPrompt");
            if (!string.IsNullOrWhiteSpace(researched))
                ctx.SetVariable("Prompt", researched);
        });
        applyResearch.Id = "apply-research";
        Pos(applyResearch, 400, 430);

        // 3c: Classifier — "is research complete?"
        var researchCheck = new ClassifierActivity
        {
            Prompt = new Input<string>(ctx =>
            {
                var researched = ctx.GetVariable<string>("ResearchedPrompt") ?? "";
                var original = ctx.GetVariable<string>("Prompt") ?? "";
                return $"Original task: {original}\n\nResearch findings so far:\n{researched}";
            }),
            ClassificationQuestion = new Input<string>(
                "Based on the research findings so far, is there still missing context about " +
                "the codebase — unexplored relevant files, unclear architecture patterns, " +
                "or requirements that need more investigation before the agent can execute " +
                "the task safely and correctly? Consider: are all affected files identified? " +
                "Are the scope boundaries clear (what to change vs what NOT to change)?"),
            ContainerId = new Input<string>(containerId),
            ModelPower = new Input<int>(2),
            Id = "research-check"
        };
        Pos(researchCheck, 400, 480);

        // --- Step 4: Execute the task ---
        var execute = new AiAssistantActivity
        {
            RunAsynchronously = true,
            AiAssistant = new Input<string>(agent),
            Prompt = new Input<string>(ctx =>
            {
                var researched = ctx.GetVariable<string>("ResearchedPrompt");
                return !string.IsNullOrWhiteSpace(researched) ? researched : ctx.GetVariable<string>("EnhancedPrompt") ?? ctx.GetVariable<string>("Prompt") ?? "";
            }),
            ContainerId = new Input<string>(containerId),
            Model = new Input<string>(model),
            ModelPower = new Input<int>(1), // Strongest for execution
            Id = "execute-agent"
        };
        Pos(execute, 400, 600);

        // --- Step 5: Verify + Repair ---
        var verifyLoop = VerifyAndRepairLoop.Create(
            verifyId: "post-verify",
            repairId: "post-repair",
            repairAgentId: "post-repair-agent",
            containerId: new Input<string>(containerId),
            originalPrompt: new Input<string>(ctx => ctx.GetVariable<string>("ResearchedPrompt") ?? ctx.GetVariable<string>("Prompt") ?? ""),
            assistant: new Input<string>(agent),
            model: new Input<string>(model),
            modelPower: new Input<int>(1));
        Pos(verifyLoop.Verify, 400, 720);
        Pos(verifyLoop.Repair, 250, 840);
        Pos(verifyLoop.RepairAgent, 400, 840);

        // --- Step 6: Website classifier ---
        var websiteClassifier = new WebsiteTaskClassifierActivity
        {
            RunAsynchronously = true,
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            IsWebsiteTask = new Output<bool>(isWebsiteTask),
            Id = "website-classifier"
        };
        Pos(websiteClassifier, 400, 960);

        // --- Step 7: Website audit loop ---
        // 7a: Iteration gate (max 5 audit passes)
        var auditGate = new IterationGateActivity
        {
            CurrentCount = new Input<int>(auditIterations),
            NextCount = new Output<int>(auditIterations),
            MaxIterations = new Input<int>(60),
            Label = new Input<string>("Website Audit Loop"),
            Id = "audit-gate"
        };
        Pos(auditGate, 400, 1060);

        // 7b: Audit runner
        var auditRunner = new AiAssistantActivity
        {
            RunAsynchronously = true,
            AiAssistant = new Input<string>(agent),
            Prompt = new Input<string>(ctx =>
            {
                var original = ctx.GetVariable<string>("Prompt") ?? "";
                return $"""
                    You are a visual QA auditor. Verify the design changes are correct.

                    Original request: {original}

                    IMPORTANT BROWSER RULES:
                    - Chromium is PRE-INSTALLED. Do NOT run 'npx playwright install' or download any browser.
                    - Use HEADED mode (headless: false) so the browser is visible on the desktop.
                    - The pre-installed Chromium is at: $PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH
                    - Environment variable PLAYWRIGHT_BROWSERS_PATH=/ms-playwright is already set.
                    - If using Playwright MCP or launch(), set headless: false explicitly.

                    AUDIT CHECKLIST:
                    1. Figure out how to run the project locally (check project files for the stack)
                    2. Open pages in headless Chromium and take screenshots
                    3. Check: layout, colors, fonts, spacing, responsive behavior
                    4. Verify the design is visually distinct from the original/base design
                    5. Fix any issues found
                    6. Take before/after screenshots as evidence
                    """;
            }),
            ContainerId = new Input<string>(containerId),
            ModelPower = new Input<int>(1),
            Id = "audit-runner"
        };
        Pos(auditRunner, 400, 1160);

        // 7c: Classifier — "are there still visual issues?"
        var auditCheck = new ClassifierActivity
        {
            Prompt = new Input<string>(ctx =>
            {
                var original = ctx.GetVariable<string>("Prompt") ?? "";
                return $"Original design request: {original}\n\nThe agent just completed an audit pass on the website.";
            }),
            ClassificationQuestion = new Input<string>(
                "After the latest audit pass, are there still visual design issues, " +
                "layout problems, broken responsive behavior, or style inconsistencies " +
                "that need to be fixed? Consider: does the page render correctly? " +
                "Is the design distinct from the original as requested?"),
            ContainerId = new Input<string>(containerId),
            ModelPower = new Input<int>(2),
            Id = "audit-check"
        };
        Pos(auditCheck, 400, 1260);

        // --- Step 8: Destroy container ---
        var destroy = new DestroyContainerActivity
        {
            ContainerId = new Input<string>(containerId),
            Id = "destroy-container"
        };
        Pos(destroy, 400, 1400);

        // --- Build Flowchart ---
        var flowchart = new Flowchart
        {
            Id = "deep-research-flow",
            Start = initVars,
            Activities =
            {
                initVars, spawn, enhance, applyEnhanced,
                researchGate, research, applyResearch, researchCheck,
                execute,
                verifyLoop.Verify, verifyLoop.Repair, verifyLoop.RepairAgent,
                websiteClassifier,
                auditGate, auditRunner, auditCheck,
                destroy
            },
            Connections =
            {
                // Init → Spawn → Enhance → Apply Enhanced → Research Gate
                new Connection(new Endpoint(initVars), new Endpoint(spawn)),
                new Connection(new Endpoint(spawn, "Done"), new Endpoint(enhance)),
                new Connection(new Endpoint(spawn, "Failed"), new Endpoint(destroy)),
                new Connection(new Endpoint(enhance, "Done"), new Endpoint(applyEnhanced)),
                new Connection(new Endpoint(enhance, "Failed"), new Endpoint(applyEnhanced)),
                new Connection(new Endpoint(applyEnhanced), new Endpoint(researchGate)),

                // === RESEARCH LOOP ===
                // Gate Continue → Research Pass → Apply Research → Classifier
                new Connection(new Endpoint(researchGate, "Continue"), new Endpoint(research)),
                new Connection(new Endpoint(researchGate, "Exceeded"), new Endpoint(execute)),
                new Connection(new Endpoint(research, "Done"), new Endpoint(applyResearch)),
                new Connection(new Endpoint(research, "Failed"), new Endpoint(execute)),
                new Connection(new Endpoint(applyResearch), new Endpoint(researchCheck)),
                // Classifier True (still missing) → LOOP BACK to research gate
                new Connection(new Endpoint(researchCheck, "True"), new Endpoint(researchGate)),
                // Classifier False (research complete) → proceed to Execute
                new Connection(new Endpoint(researchCheck, "False"), new Endpoint(execute)),
                // Classifier failed → proceed to Execute
                new Connection(new Endpoint(researchCheck, "Failed"), new Endpoint(execute)),

                // === EXECUTE ===
                // Execute → Verify
                new Connection(new Endpoint(execute, "Done"), new Endpoint(verifyLoop.Verify)),
                new Connection(new Endpoint(execute, "Failed"), new Endpoint(websiteClassifier)),
                // Verify passed/inconclusive → Website Classifier
                new Connection(new Endpoint(verifyLoop.Verify, "Passed"), new Endpoint(websiteClassifier)),
                new Connection(new Endpoint(verifyLoop.Verify, "Inconclusive"), new Endpoint(websiteClassifier)),
                // Repair exceeded → Website Classifier (proceed anyway)
                new Connection(new Endpoint(verifyLoop.Repair, "Exceeded"), new Endpoint(websiteClassifier)),

                // === WEBSITE ROUTING ===
                // Website → Audit Gate
                new Connection(new Endpoint(websiteClassifier, "Website"), new Endpoint(auditGate)),
                // NonWebsite → Destroy (skip audit)
                new Connection(new Endpoint(websiteClassifier, "NonWebsite"), new Endpoint(destroy)),

                // === WEBSITE AUDIT LOOP ===
                // Gate Continue → Audit Runner
                new Connection(new Endpoint(auditGate, "Continue"), new Endpoint(auditRunner)),
                // Gate Exceeded → Destroy (done)
                new Connection(new Endpoint(auditGate, "Exceeded"), new Endpoint(destroy)),
                // Audit Runner → Classifier check
                new Connection(new Endpoint(auditRunner, "Done"), new Endpoint(auditCheck)),
                new Connection(new Endpoint(auditRunner, "Failed"), new Endpoint(destroy)),
                // Classifier True (issues remain) → LOOP BACK to audit gate
                new Connection(new Endpoint(auditCheck, "True"), new Endpoint(auditGate)),
                // Classifier False (audit complete) → Destroy
                new Connection(new Endpoint(auditCheck, "False"), new Endpoint(destroy)),
                new Connection(new Endpoint(auditCheck, "Failed"), new Endpoint(destroy)),
            }
        };

        // Add VerifyAndRepairLoop internal connections
        foreach (var conn in verifyLoop.InternalConnections)
            flowchart.Connections.Add(conn);

        builder.Root = flowchart.WithAttachedVariables(builder);
    }
}
