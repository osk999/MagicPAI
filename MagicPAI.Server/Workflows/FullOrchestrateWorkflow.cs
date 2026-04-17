using Elsa.Expressions.Models;
using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Activities;
using Elsa.Workflows.Activities.Flowchart.Models;
using Elsa.Workflows.Models;
using Elsa.Workflows.Runtime.Activities;
using MagicPAI.Activities.AI;
using MagicPAI.Activities.Docker;

namespace MagicPAI.Server.Workflows;

/// <summary>
/// Parent orchestration workflow that owns the container lifecycle and delegates
/// specialized execution to reusable child workflows.
/// </summary>
public class FullOrchestrateWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        builder.Name = "Full Orchestrate";
        builder.Description =
            "Complete AI orchestration: website routing, research prompt grounding, child workflow execution, and cleanup";

        var prompt = builder.WithVariable<string>("Prompt", "").WithWorkflowStorage();
        var containerId = builder.WithVariable<string>("ContainerId", "").WithWorkflowStorage();
        var assistant = builder.WithVariable<string>("AiAssistant", "").WithWorkflowStorage();
        var model = builder.WithVariable<string>("Model", "").WithWorkflowStorage();
        var modelPower = builder.WithVariable<int>("ModelPower", 0).WithWorkflowStorage();
        var recommendedModel = builder.WithVariable<string>("RecommendedModel", "").WithWorkflowStorage();
        var isWebsiteTask = builder.WithVariable<bool>("IsWebsiteTask", false).WithWorkflowStorage();
        var researchedPrompt = builder.WithVariable<string>("ResearchedPrompt", "").WithWorkflowStorage();

        // Initialize variables from dispatch input at workflow start.
        // Elsa variables with the same name as input keys shadow the input,
        // so we copy input to variables explicitly.
        var initVars = new Elsa.Workflows.Activities.Inline(ctx =>
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
                else if (mp is long l) ctx.SetVariable("ModelPower", (int)l);
                else if (int.TryParse(mp?.ToString(), out var pi)) ctx.SetVariable("ModelPower", pi);
            }
        });
        initVars.Id = "init-vars";
        Pos(initVars, 400, 10);

        Input<string> resolveContainerId() => new(ctx => ctx.Resolve("ContainerId"));

        Input<string> resolveBestPrompt() => new(ctx =>
        {
            var best = ctx.GetVariable<string>("ResearchedPrompt");
            if (!string.IsNullOrWhiteSpace(best)) return best;
            best = ctx.GetVariable<string>("GatheredContext");
            if (!string.IsNullOrWhiteSpace(best)) return best;
            best = ctx.GetVariable<string>("ElaboratedPrompt");
            if (!string.IsNullOrWhiteSpace(best)) return best;
            best = ctx.GetVariable<string>("EnhancedPrompt");
            if (!string.IsNullOrWhiteSpace(best)) return best;
            return ctx.GetDispatchInput("Prompt") ?? "";
        });

        Input<IDictionary<string, object>?> buildChildInput() => new(ctx =>
        {
            var resolvedAssistant = ctx.ResolveFirst("", "AiAssistant", "Agent");
            var requestedModel = ctx.GetDispatchInput("Model");
            var resolvedModel =
                string.IsNullOrWhiteSpace(requestedModel) ||
                string.Equals(requestedModel, "auto", StringComparison.OrdinalIgnoreCase)
                    ? ctx.GetVariable<string>("RecommendedModel")
                        ?? ctx.GetVariable<string>("Model")
                        ?? ""
                    : requestedModel;

            var bestPrompt = ctx.GetVariable<string>("GatheredContext");
            if (string.IsNullOrWhiteSpace(bestPrompt))
                bestPrompt = ctx.GetVariable<string>("ElaboratedPrompt");
            if (string.IsNullOrWhiteSpace(bestPrompt))
                bestPrompt = ctx.GetVariable<string>("EnhancedPrompt");
            if (string.IsNullOrWhiteSpace(bestPrompt))
                bestPrompt = ctx.GetDispatchInput("Prompt") ?? "";

            return new Dictionary<string, object>
            {
                ["Prompt"] = bestPrompt,
                ["AiAssistant"] = resolvedAssistant,
                ["Agent"] = resolvedAssistant,
                ["Model"] = resolvedModel,
                ["ModelPower"] = ctx.GetVariable<int>("ModelPower"),
                ["ContainerId"] = ctx.GetVariable<string>("ContainerId") ?? "",
                ["EnableGui"] = ctx.GetDispatchInput<bool?>("EnableGui") ?? true
            };
        });

        var spawn = new SpawnContainerActivity
        {
            WorkspacePath = new Input<string>(""),
            EnableGui = new Input<bool>(ctx => ctx.GetInput<bool?>("EnableGui") ?? true),
            ContainerId = new Output<string>(containerId),
            Id = "spawn-container"
        };
        Pos(spawn, 400, 50);

        var websiteClassifier = new WebsiteTaskClassifierActivity
        {
            RunAsynchronously = true,
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            IsWebsiteTask = new Output<bool>(isWebsiteTask),
            Id = "website-classifier"
        };
        Pos(websiteClassifier, 400, 220);

        var researchPrompt = new ResearchPromptActivity
        {
            RunAsynchronously = true,
            AiAssistant = new Input<string>(""),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            ModelPower = new Input<int>(2),
            EnhancedPrompt = new Output<string>(researchedPrompt),
            Id = "research-prompt"
        };
        Pos(researchPrompt, 400, 390);

        var triage = new TriageActivity
        {
            RunAsynchronously = true,
            Prompt = resolveBestPrompt(),
            ContainerId = new Input<string>(containerId),
            RecommendedModel = new Output<string>(recommendedModel),
            RecommendedModelPower = new Output<int>(modelPower),
            Id = "triage"
        };
        Pos(triage, 400, 560);

        var simplePath = new ExecuteWorkflow
        {
            WorkflowDefinitionId = new Input<string>(nameof(OrchestrateSimplePathWorkflow)),
            WaitForCompletion = new Input<bool>(true),
            Input = buildChildInput(),
            Id = "simple-path"
        };
        Pos(simplePath, 250, 720);

        // Store child input in SharedBlackboard. Elsa's DispatchWorkflow/ExecuteWorkflow
        // does NOT reliably propagate Input to the child's WorkflowInput, so every
        // child workflow (OrchestrateComplexPath, OrchestrateSimplePath via ExecuteWorkflow,
        // WebsiteAuditCore) reads a JSON payload from SharedBlackboard keyed by this
        // parent's instance id. We store TWICE: once right after spawn with raw
        // ContainerId+Prompt (for early dispatches like audit/simple that may fire
        // before research enhancements exist), and again before the complex dispatch
        // with the research-enhanced prompt.
        static Elsa.Workflows.Activities.Inline BuildStoreChildInput(string id)
        {
            var inline = new Elsa.Workflows.Activities.Inline(ctx =>
            {
                var bb = ctx.GetRequiredService<MagicPAI.Core.Services.SharedBlackboard>();
                var parentId = ctx.WorkflowExecutionContext.Id;

                var bestPrompt = ctx.GetVariable<string>("ResearchedPrompt");
                if (string.IsNullOrWhiteSpace(bestPrompt)) bestPrompt = ctx.GetVariable<string>("GatheredContext");
                if (string.IsNullOrWhiteSpace(bestPrompt)) bestPrompt = ctx.GetVariable<string>("ElaboratedPrompt");
                if (string.IsNullOrWhiteSpace(bestPrompt)) bestPrompt = ctx.GetVariable<string>("EnhancedPrompt");
                if (string.IsNullOrWhiteSpace(bestPrompt)) bestPrompt = ctx.GetVariable<string>("Prompt") ?? "";

                var data = new Dictionary<string, object>
                {
                    ["Prompt"] = bestPrompt,
                    ["ContainerId"] = ctx.GetVariable<string>("ContainerId") ?? "",
                    ["AiAssistant"] = ctx.GetVariable<string>("AiAssistant") ?? "",
                    ["Model"] = ctx.GetVariable<string>("Model") ?? "",
                    ["ModelPower"] = ctx.GetVariable<int>("ModelPower")
                };
                bb.SetTaskOutput($"{parentId}:child-input",
                    System.Text.Json.JsonSerializer.Serialize(data));
            });
            inline.Id = id;
            return inline;
        }

        var storeChildInputEarly = BuildStoreChildInput("store-child-input-early");
        Pos(storeChildInputEarly, 400, 120);

        var storeChildInput = BuildStoreChildInput("store-child-input");
        Pos(storeChildInput, 550, 700);

        var complexPath = new Elsa.Workflows.Runtime.Activities.DispatchWorkflow
        {
            WorkflowDefinitionId = new Input<string>(nameof(OrchestrateComplexPathWorkflow)),
            WaitForCompletion = new Input<bool>(true),
            Input = buildChildInput(),
            Id = "complex-path"
        };
        Pos(complexPath, 550, 780);

        // Requirements-coverage classifier: grades the completed work against the
        // original user requirements. On Incomplete it sets RepairPrompt and routes
        // to coverageRepairAgent, which re-runs Claude with the focused gap prompt
        // (Claude's own session is resumed so it keeps context). After that, we come
        // back to coverage to re-verify. Capped at 30 iterations.
        var coverage = new RequirementsCoverageActivity
        {
            RunAsynchronously = true,
            OriginalPrompt = new Input<string>(prompt),
            ContainerId = resolveContainerId(),
            MaxIterations = new Input<int>(30),
            ModelPower = new Input<int>(2),
            Id = "requirements-coverage"
        };
        Pos(coverage, 400, 970);

        var coverageRepairAgent = new AiAssistantActivity
        {
            RunAsynchronously = true,
            AiAssistant = new Input<string>(new Expression("JavaScript", "getVariable(\"AiAssistant\") || \"claude\"")),
            Prompt = new Input<string>(new Expression("JavaScript", "getVariable(\"RepairPrompt\") || \"\"")),
            ContainerId = resolveContainerId(),
            ModelPower = new Input<int>(2),
            Id = "coverage-repair-agent"
        };
        Pos(coverageRepairAgent, 600, 970);

        var destroy = new DestroyContainerActivity
        {
            ContainerId = resolveContainerId(),
            Id = "destroy-container"
        };
        Pos(destroy, 400, 1100);

        var flowchart = new Flowchart
        {
            Id = "full-orchestrate-flow",
            Start = initVars,
            Activities = { initVars, spawn, storeChildInputEarly, websiteClassifier, researchPrompt, triage, simplePath, storeChildInput, complexPath, coverage, coverageRepairAgent, destroy },
            Connections =
            {
                new Connection(new Endpoint(initVars), new Endpoint(spawn)),
                // Populate SharedBlackboard right after spawn so any child dispatch
                // (simple path via ExecuteWorkflow, website audit via ExecuteWorkflow)
                // can read ContainerId + Prompt on first activity. The second store
                // later (before complexPath) overwrites with research-enhanced prompt.
                new Connection(new Endpoint(spawn, "Done"), new Endpoint(storeChildInputEarly)),
                new Connection(new Endpoint(storeChildInputEarly), new Endpoint(websiteClassifier)),
                new Connection(new Endpoint(spawn, "Failed"), new Endpoint(destroy)),

                // Both website and non-website implementation requests go through the main
                // execution pipeline. Website work gets an audit pass after implementation.
                new Connection(new Endpoint(websiteClassifier, "Website"), new Endpoint(researchPrompt)),
                new Connection(new Endpoint(websiteClassifier, "NonWebsite"), new Endpoint(researchPrompt)),

                new Connection(new Endpoint(researchPrompt, "Done"), new Endpoint(triage)),
                new Connection(new Endpoint(researchPrompt, "Failed"), new Endpoint(triage)),

                new Connection(new Endpoint(triage, "Simple"), new Endpoint(simplePath)),
                new Connection(new Endpoint(triage, "Complex"), new Endpoint(storeChildInput)),
                new Connection(new Endpoint(storeChildInput), new Endpoint(complexPath)),

                // All paths converge on the coverage classifier. Previously website tasks
                // went through WebsiteAuditCoreWorkflow first, but that child workflow
                // hangs on a pre-existing Elsa BackgroundActivity bookmark-release bug.
                // Coverage instructs Claude to visually verify via Playwright MCP in its
                // gap_prompt, so the audit pass is redundant.
                new Connection(new Endpoint(simplePath, "Done"), new Endpoint(coverage)),
                new Connection(new Endpoint(complexPath, "Done"), new Endpoint(coverage)),

                // Coverage loop: incomplete -> run agent again with gap prompt -> re-check.
                new Connection(new Endpoint(coverage, "AllMet"), new Endpoint(destroy)),
                new Connection(new Endpoint(coverage, "Exceeded"), new Endpoint(destroy)),
                new Connection(new Endpoint(coverage, "Incomplete"), new Endpoint(coverageRepairAgent)),
                new Connection(new Endpoint(coverageRepairAgent, "Done"), new Endpoint(coverage)),
                new Connection(new Endpoint(coverageRepairAgent, "Failed"), new Endpoint(coverage)),
            }
        };

        builder.Root = flowchart.WithAttachedVariables(builder);
    }
}
