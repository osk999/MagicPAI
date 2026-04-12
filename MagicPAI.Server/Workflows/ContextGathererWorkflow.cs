using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Models;
using MagicPAI.Activities.AI;

namespace MagicPAI.Server.Workflows;

/// <summary>
/// Parallel context collection: research, repo-map analysis, and memory loading run in parallel,
/// then merge results. Uses AiAssistantActivity with different prompts for each collection phase.
/// </summary>
public class ContextGathererWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        builder.Name = "Context Gatherer";
        builder.Description =
            "Collect context in parallel: research, repo-map, and memory, then merge";

        var prompt = builder.WithVariable<string>("Prompt", "");
        var agent = builder.WithVariable<string>("AiAssistant", "claude");
        var containerId = builder.WithVariable<string>("ContainerId", "");
        var researchContext = builder.WithVariable<string>("ResearchContext", "");
        var repoMapContext = builder.WithVariable<string>("RepoMapContext", "");
        var memoryContext = builder.WithVariable<string>("MemoryContext", "");
        var mergedContext = builder.WithVariable<string>("MergedContext", "");

        // Parallel branch 1: Research
        var research = new AiAssistantActivity
        {
            AiAssistant = new Input<string>(agent),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            ModelPower = new Input<int>(3),
            Response = new Output<string>(researchContext),
            Id = "research-context"
        };
        Pos(research, 100, 50);

        // Parallel branch 2: Repo-map analysis
        var repoMap = new AiAssistantActivity
        {
            AiAssistant = new Input<string>(agent),
            Prompt = new Input<string>(ctx =>
                $"Create a repo-map style implementation context for this task:\n\n{ctx.GetVariable<string>("Prompt") ?? ""}"),
            ContainerId = new Input<string>(containerId),
            ModelPower = new Input<int>(3),
            Response = new Output<string>(repoMapContext),
            Id = "repo-map-context"
        };
        Pos(repoMap, 400, 50);

        // Parallel branch 3: Memory loading
        var memoryLoad = new AiAssistantActivity
        {
            AiAssistant = new Input<string>(agent),
            Prompt = new Input<string>(ctx =>
                $"Load any relevant prior constraints, conventions, or remembered context for this task:\n\n{ctx.GetVariable<string>("Prompt") ?? ""}"),
            ContainerId = new Input<string>(containerId),
            ModelPower = new Input<int>(3),
            Response = new Output<string>(memoryContext),
            Id = "memory-context"
        };
        Pos(memoryLoad, 700, 50);

        // Merge step: combine all context
        var mergeContext = new AiAssistantActivity
        {
            AiAssistant = new Input<string>(agent),
            Prompt = new Input<string>(ctx =>
                $$"""
                Combine the parallel context-gathering results into one concise execution brief.

                ## Original Prompt
                {{ctx.GetVariable<string>("Prompt") ?? ""}}

                ## Research
                {{ctx.GetVariable<string>("ResearchContext") ?? ""}}

                ## Repo Map
                {{ctx.GetVariable<string>("RepoMapContext") ?? ""}}

                ## Memory
                {{ctx.GetVariable<string>("MemoryContext") ?? ""}}
                """),
            ContainerId = new Input<string>(containerId),
            ModelPower = new Input<int>(2),
            Response = new Output<string>(mergedContext),
            Id = "merge-context"
        };
        Pos(mergeContext, 400, 220);

        builder.Root = new Sequence
        {
            Activities =
            {
                new Elsa.Workflows.Activities.Parallel(new IActivity[] { research, repoMap, memoryLoad }),
                mergeContext
            }
        }.WithAttachedVariables(builder);
    }
}
