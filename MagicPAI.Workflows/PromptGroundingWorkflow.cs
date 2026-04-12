using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Activities;
using Elsa.Workflows.Activities.Flowchart.Models;
using Elsa.Workflows.Models;
using MagicPAI.Activities.AI;

namespace MagicPAI.Workflows;

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
        var containerId = builder.WithVariable<string>("ContainerId", "");

        // Step 1: Analyze the codebase structure
        var analyzeCodebase = new RunCliAgentActivity
        {
            Agent = new Input<string>("claude"),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Model = new Input<string>("sonnet"),
            Id = "analyze-codebase"
        };

        // Step 2: Rewrite the prompt with grounded context
        var rewritePrompt = new RunCliAgentActivity
        {
            Agent = new Input<string>("claude"),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Model = new Input<string>("sonnet"),
            Id = "rewrite-prompt"
        };

        var flowchart = new Flowchart
        {
            Id = "prompt-grounding-flow",
            Start = analyzeCodebase,
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

        builder.Root = flowchart;
    }
}
