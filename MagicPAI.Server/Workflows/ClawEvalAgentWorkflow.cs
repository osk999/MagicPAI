using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Activities;
using Elsa.Workflows.Activities.Flowchart.Models;
using Elsa.Workflows.Models;
using MagicPAI.Activities.AI;

namespace MagicPAI.Server.Workflows;

/// <summary>
/// Benchmark agent-task workflow for evaluation.
/// Flow: Triage (classify difficulty) -> Context Gathering -> Agent Execution.
/// Used in benchmark/evaluation suites to measure agent task completion quality.
/// </summary>
public class ClawEvalAgentWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        builder.Name = "Claw Eval Agent";
        builder.Description =
            "Benchmark agent workflow: triage, context-gathering, and agent execution for evaluation";

        var prompt = builder.WithVariable<string>("Prompt", "");
        var containerId = builder.WithVariable<string>("ContainerId", "");
        var agent = builder.WithVariable<string>("AiAssistant", "claude");
        var model = builder.WithVariable<string>("Model", "auto");
        var modelPower = builder.WithVariable<int>("ModelPower", 0);

        // Step 1: Triage to classify task difficulty
        var triage = new TriageActivity
        {
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Id = "eval-triage"
        };

        // Step 2: Context gathering
        var gatherContext = new AiAssistantActivity
        {
            AiAssistant = new Input<string>(agent),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            ModelPower = new Input<int>(3),
            Id = "eval-context"
        };

        // Step 3a: Simple execution
        var simpleExec = new AiAssistantActivity
        {
            AiAssistant = new Input<string>(agent),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Model = new Input<string>(model),
            ModelPower = new Input<int>(modelPower),
            Id = "eval-simple-exec"
        };

        // Step 3b: Complex execution (uses Opus)
        var complexExec = new AiAssistantActivity
        {
            AiAssistant = new Input<string>(agent),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            ModelPower = new Input<int>(1),
            Id = "eval-complex-exec"
        };

        var flowchart = new Flowchart
        {
            Id = "claw-eval-agent-flow",
            Start = triage,
            Activities = { triage, gatherContext, simpleExec, complexExec },
            Connections =
            {
                // Triage Simple -> context gathering -> simple exec
                new Connection(
                    new Endpoint(triage, "Simple"),
                    new Endpoint(gatherContext)),

                // Triage Complex -> context gathering (same entry point)
                new Connection(
                    new Endpoint(triage, "Complex"),
                    new Endpoint(gatherContext)),

                // Context done -> simple execution (default path)
                new Connection(
                    new Endpoint(gatherContext, "Done"),
                    new Endpoint(simpleExec)),

                // Context failed -> simple execution anyway
                new Connection(
                    new Endpoint(gatherContext, "Failed"),
                    new Endpoint(simpleExec)),
            }
        };

        builder.Root = flowchart.WithAttachedVariables(builder);
    }
}
