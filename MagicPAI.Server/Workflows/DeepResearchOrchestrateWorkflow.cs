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

        // --- Step 7: Website audit loop (strict visual QA) ---
        var auditGate = new IterationGateActivity
        {
            CurrentCount = new Input<int>(auditIterations),
            NextCount = new Output<int>(auditIterations),
            MaxIterations = new Input<int>(60),
            Label = new Input<string>("Website Audit Loop"),
            Id = "audit-gate"
        };
        Pos(auditGate, 400, 1060);

        var auditRunner = new AiAssistantActivity
        {
            RunAsynchronously = true,
            AiAssistant = new Input<string>(agent),
            Prompt = new Input<string>(ctx =>
            {
                var original = ctx.GetVariable<string>("Prompt") ?? "";
                return $"""
                    You are a STRICT visual QA auditor and designer. Your job is to open the
                    website in a REAL browser, visually inspect every page, and fix every issue.

                    Original request: {original}

                    ## BROWSER RULES (MANDATORY)
                    - Chromium is PRE-INSTALLED at $PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH
                    - Do NOT run 'npx playwright install' or download any browser
                    - Use Playwright MCP tools (mcp__playwright__*) for all browser operations
                    - Browser runs HEADED on DISPLAY=:99 (visible on noVNC desktop)
                    - PLAYWRIGHT_BROWSERS_PATH=/ms-playwright is set
                    - Take screenshots to /workspace/screenshots/ as evidence

                    ## STEP-BY-STEP AUDIT PROCESS

                    ### 1. Start the project server
                    - Figure out how to run the project (check for .csproj, package.json, etc.)
                    - Start it on a local port (e.g., dotnet run --urls=http://localhost:5199)
                    - Wait for it to be ready

                    ### 2. Open EVERY key page in the browser
                    - Use mcp__playwright__browser_navigate to visit each page
                    - Take a screenshot of EACH page using mcp__playwright__browser_take_screenshot
                    - Save screenshots to /workspace/screenshots/

                    ### 3. Inspect EACH of these visual elements critically
                    For each page, check and grade (PASS/FAIL) each item:

                    **Colors & Palette:**
                    - Are colors genuinely different from the base/default theme?
                    - Not just a hue shift — a completely different palette family?
                    - Does the color scheme feel cohesive and intentional?
                    - Check: primary, secondary, accent, background, surface, text colors

                    **Typography:**
                    - Different font family from the base theme?
                    - Different heading hierarchy (sizes, weights)?
                    - Does text have good contrast against backgrounds?

                    **Layout & Spacing:**
                    - Different spacing rhythm (not same 8px/16px/24px pattern)?
                    - Different card/container treatments (shadows, borders, radius)?
                    - Does the page feel like a different design system?

                    **Components:**
                    - Buttons: different shape, color, hover effect?
                    - Cards: different shadow, border, radius?
                    - Navigation: different style?
                    - Forms: different input styling?

                    **Responsive:**
                    - Check at 375px mobile width — does it work?
                    - Check at 768px tablet — does it adapt?
                    - No horizontal scrolling?

                    **Overall Impression:**
                    - If you showed both pages to a random person, would they think
                      they're from the SAME website? If YES → FAIL, fix it.

                    ### 4. Fix EVERY issue found
                    - Do NOT just report issues — FIX them in the code
                    - After fixing, rebuild and re-check in the browser
                    - Take new screenshots showing the fix worked
                    - "Do not stop at reporting if a fix is feasible"

                    ### 5. Report format
                    For each page:
                    VISUAL_PASS: [page] — [what works]
                    VISUAL_ISSUE: [critical|major|minor] [page] — [problem + fix applied]

                    End with:
                    VISUAL_SUMMARY: [pages] pages, [critical] critical, [major] major, [minor] minor
                    """;
            }),
            ContainerId = new Input<string>(containerId),
            ModelPower = new Input<int>(1),
            Id = "audit-runner"
        };
        Pos(auditRunner, 400, 1160);

        // Audit classifier: AiAssistantActivity with browser access (NOT text-only ClassifierActivity)
        // This agent opens the ACTUAL page in Chrome, sees it with its own eyes, and judges.
        var auditCheckOutput = builder.WithVariable<string>("AuditCheckOutput", "").WithWorkflowStorage();
        var auditCheck = new AiAssistantActivity
        {
            RunAsynchronously = true,
            AiAssistant = new Input<string>(agent),
            Prompt = new Input<string>(ctx =>
            {
                var original = ctx.GetVariable<string>("Prompt") ?? "";
                return $"""
                    You are an INDEPENDENT visual QA judge. Your job is to open the website
                    in a REAL browser, look at the actual rendered page, and honestly judge
                    whether the design work is good enough.

                    You are NOT the same agent who made the changes. You are a fresh pair of eyes.

                    Original design request: {original}

                    ## YOUR TASK

                    ### Step 1: Start the server (if not already running)
                    - Check if the project server is running (try curl localhost:5199 or 5200)
                    - If not, start it (check project files for how)
                    - Wait until it responds

                    ### Step 2: Open the page in Chrome using Playwright MCP
                    - Use mcp__playwright__browser_navigate to go to the homepage
                    - Use mcp__playwright__browser_take_screenshot to capture what you see
                    - Navigate to at least 2-3 key pages and screenshot each

                    ### Step 3: Judge HONESTLY — grade each area PASS or FAIL

                    Look at each screenshot and answer:

                    **Colors**: Are they genuinely different from a typical blue/teal donation site?
                    Not just darker/lighter — a DIFFERENT color family? PASS/FAIL

                    **Typography**: Can you see different fonts? Different heading sizes/weights?
                    Does it feel like different typography? PASS/FAIL

                    **Layout**: Does the page structure look different? Different card shapes,
                    different spacing, different visual rhythm? PASS/FAIL

                    **Buttons & Components**: Different button styles? Different form inputs?
                    Different navigation? PASS/FAIL

                    **Overall**: If you showed this page and the default SimpleDonate page to
                    your grandmother, would she say "these are different websites"? PASS/FAIL

                    **Quality**: Does the page look GOOD? No broken layouts, no ugly spacing,
                    no unreadable text, no overlapping elements? PASS/FAIL

                    ### Step 4: Return your verdict as JSON

                    Respond with ONLY structured JSON matching the provided schema.
                    Fields: needsMoreWork (bool), passCount (int 0-6), failCount (int 0-6),
                    failedAreas (array of strings), screenshotsTaken (int), rationale (string).

                    RULES:
                    - needsMoreWork = true if ANY area is FAIL
                    - needsMoreWork = false ONLY if ALL 6 areas PASS
                    - You MUST take at least 2 screenshots before judging
                    - If you cannot open the page, needsMoreWork = true
                    - Be STRICT — mediocre design that "sort of works" is a FAIL
                    """;
            }),
            ContainerId = new Input<string>(containerId),
            ModelPower = new Input<int>(1),
            StructuredOutputSchema = new Input<string>("""{"type":"object","properties":{"needsMoreWork":{"type":"boolean"},"passCount":{"type":"integer"},"failCount":{"type":"integer"},"failedAreas":{"type":"array","items":{"type":"string"}},"screenshotsTaken":{"type":"integer"},"rationale":{"type":"string"}},"required":["needsMoreWork","passCount","failCount","failedAreas","screenshotsTaken","rationale"]}"""),
            Response = new Output<string>(auditCheckOutput),
            Id = "audit-check"
        };
        Pos(auditCheck, 400, 1260);

        // Parse audit-check result and route: needsMoreWork → loop back, else → done
        var auditDecision = new FlowDecision(ctx =>
        {
            var output = ctx.GetVariable<string>("AuditCheckOutput") ?? "";
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(output);
                if (doc.RootElement.TryGetProperty("needsMoreWork", out var nmw))
                    return nmw.GetBoolean();
            }
            catch { /* parse failed = needs more work */ }
            return true; // default: needs more work
        });
        auditDecision.Id = "audit-decision";
        Pos(auditDecision, 400, 1340);

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
                auditGate, auditRunner, auditCheck, auditDecision,
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
                // Gate Continue → Audit Runner → Audit Check (browser-based) → Decision
                new Connection(new Endpoint(auditGate, "Continue"), new Endpoint(auditRunner)),
                new Connection(new Endpoint(auditGate, "Exceeded"), new Endpoint(destroy)),
                new Connection(new Endpoint(auditRunner, "Done"), new Endpoint(auditCheck)),
                new Connection(new Endpoint(auditRunner, "Failed"), new Endpoint(destroy)),
                // Audit Check (AiAssistant with browser) → Decision
                new Connection(new Endpoint(auditCheck, "Done"), new Endpoint(auditDecision)),
                new Connection(new Endpoint(auditCheck, "Failed"), new Endpoint(destroy)),
                // Decision: True (needsMoreWork) → LOOP BACK to audit gate
                new Connection(new Endpoint(auditDecision, "True"), new Endpoint(auditGate)),
                // Decision: False (all good) → Destroy
                new Connection(new Endpoint(auditDecision, "False"), new Endpoint(destroy)),
            }
        };

        // Add VerifyAndRepairLoop internal connections
        foreach (var conn in verifyLoop.InternalConnections)
            flowchart.Connections.Add(conn);

        builder.Root = flowchart.WithAttachedVariables(builder);
    }
}
