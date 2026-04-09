using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Activities;
using Elsa.Workflows.Activities.Flowchart.Models;
using Elsa.Workflows.Models;
using MagicPAI.Activities.AI;

namespace MagicPAI.Server.Workflows;

/// <summary>
/// Research-first prompt grounding: analyze codebase then rewrite vague intent
/// into a precise, file-specific implementation prompt.
/// Flow: Analyze Codebase -> Rewrite Prompt -> Output grounded prompt
/// </summary>
public class PromptGroundingWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        builder.Name = "Prompt Grounding";
        builder.Description =
            "Analyze codebase and rewrite vague prompts into precise, file-specific instructions";

        var prompt = builder.WithVariable<string>("Prompt", "");
        var agent = builder.WithVariable<string>("AiAssistant", "claude");
        var containerId = builder.WithVariable<string>("ContainerId", "");
        var codebaseAnalysis = builder.WithVariable<string>("CodebaseAnalysis", "");
        var groundedPrompt = builder.WithVariable<string>("GroundedPrompt", "");

        // Step 1: Analyze the codebase structure
        var analyzeCodebase = new AiAssistantActivity
        {
            AiAssistant = new Input<string>(agent),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            ModelPower = new Input<int>(3),
            Response = new Output<string>(codebaseAnalysis),
            Id = "analyze-codebase"
        };

        // Step 2: Rewrite the prompt with grounded context
        var rewritePrompt = new AiAssistantActivity
        {
            AiAssistant = new Input<string>(agent),
            Prompt = new Input<string>(ctx =>
                $$"""
                Rewrite this task into a precise, implementation-ready prompt using the codebase analysis below.

                ## Original Prompt
                {{ctx.GetVariable<string>("Prompt") ?? ""}}

                ## Codebase Analysis
                {{ctx.GetVariable<string>("CodebaseAnalysis") ?? ""}}
                """),
            ContainerId = new Input<string>(containerId),
            ModelPower = new Input<int>(2),
            Response = new Output<string>(groundedPrompt),
            Id = "rewrite-prompt"
        };

        var flowchart = new Flowchart
        {
            Id = "prompt-grounding-flow",
            Start = analyzeCodebase,
            Activities = { analyzeCodebase, rewritePrompt },
            Connections =
            {
                new Connection(
                    new Endpoint(analyzeCodebase, "Done"),
                    new Endpoint(rewritePrompt)),

                // If analysis fails, still attempt rewrite with original prompt
                new Connection(
                    new Endpoint(analyzeCodebase, "Failed"),
                    new Endpoint(rewritePrompt)),
            }
        };

        builder.Root = flowchart.WithAttachedVariables(builder);
    }
}
