using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Activities;
using Elsa.Workflows.Activities.Flowchart.Models;
using Elsa.Workflows.Models;
using MagicPAI.Activities.AI;

namespace MagicPAI.Workflows;

/// <summary>
/// Parallel context collection: research, repo-map analysis, and memory loading run in parallel,
/// then merge results. Uses RunCliAgentActivity with different prompts for each collection phase.
/// </summary>
public class ContextGathererWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        builder.Name = "Context Gatherer";
        builder.Description =
            "Collect context in parallel: research, repo-map, and memory, then merge";

        var prompt = builder.WithVariable<string>("Prompt", "");
        var containerId = builder.WithVariable<string>("ContainerId", "");

        // Parallel branch 1: Research
        var research = new RunCliAgentActivity
        {
            Agent = new Input<string>("claude"),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Model = new Input<string>("haiku"),
            Id = "research-context"
        };

        // Parallel branch 2: Repo-map analysis
        var repoMap = new RunCliAgentActivity
        {
            Agent = new Input<string>("claude"),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Model = new Input<string>("haiku"),
            Id = "repo-map-context"
        };

        // Parallel branch 3: Memory loading
        var memoryLoad = new RunCliAgentActivity
        {
            Agent = new Input<string>("claude"),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Model = new Input<string>("haiku"),
            Id = "memory-context"
        };

        // Merge step: combine all context
        var mergeContext = new RunCliAgentActivity
        {
            Agent = new Input<string>("claude"),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Model = new Input<string>("sonnet"),
            Id = "merge-context"
        };

        // In Elsa Flowchart, activities with no inbound connections from Start
        // run in parallel when reached. We use a fork-join pattern:
        // All three branches connect to merge.
        var flowchart = new Flowchart
        {
            Id = "context-gatherer-flow",
            Start = research,
            Connections =
            {
                // Research done -> merge
                new Connection(
                    new Endpoint(research, "Done"),
                    new Endpoint(mergeContext)),

                // Research failed -> merge (proceed with partial context)
                new Connection(
                    new Endpoint(research, "Failed"),
                    new Endpoint(mergeContext)),

                // Repo-map done -> merge
                new Connection(
                    new Endpoint(repoMap, "Done"),
                    new Endpoint(mergeContext)),

                // Repo-map failed -> merge
                new Connection(
                    new Endpoint(repoMap, "Failed"),
                    new Endpoint(mergeContext)),

                // Memory done -> merge
                new Connection(
                    new Endpoint(memoryLoad, "Done"),
                    new Endpoint(mergeContext)),

                // Memory failed -> merge
                new Connection(
                    new Endpoint(memoryLoad, "Failed"),
                    new Endpoint(mergeContext)),
            }
        };

        builder.Root = flowchart;
    }
}
