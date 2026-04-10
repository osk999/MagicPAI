using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Activities;
using Elsa.Workflows.Activities.Flowchart.Models;
using Elsa.Workflows.Models;
using MagicPAI.Activities.AI;
using MagicPAI.Activities.ControlFlow;
using MagicPAI.Activities.Docker;

namespace MagicPAI.Server.Workflows;

/// <summary>
/// Generic reusable loop: runner executes, classifier checks for [DONE] marker,
/// exits or iterates up to max iterations. Uses AiAssistantActivity + TriageActivity.
/// The loop-back is modeled as a Flowchart connection from triage back to the runner.
/// </summary>
public class LoopVerifierWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        builder.Name = "Loop Verifier";
        builder.Description =
            "Generic execution loop: run agent, check completion, iterate until done or max attempts";

        var prompt = builder.WithVariable<string>("Prompt", "");
        var containerId = builder.WithVariable<string>("ContainerId", "");
        var agent = builder.WithVariable<string>("AiAssistant", "claude");
        var model = builder.WithVariable<string>("Model", "auto");
        var modelPower = builder.WithVariable<int>("ModelPower", 0);
        var loopOutput = builder.WithVariable<string>("LoopOutput", "");
        var attemptCount = builder.WithVariable<int>("LoopAttempts", 0);

        var spawn = new SpawnContainerActivity
        {
            WorkspacePath = new Input<string>(""),
            ContainerId = new Output<string>(containerId),
            Id = "loop-spawn"
        };

        var iterationGate = new IterationGateActivity
        {
            CurrentCount = new Input<int>(attemptCount),
            NextCount = new Output<int>(attemptCount),
            MaxIterations = new Input<int>(ctx => Math.Max(1, ctx.GetInput<int?>("MaxTurns") ?? 3)),
            Label = new Input<string>("Loop Verifier"),
            Id = "loop-iteration-gate"
        };

        // Step 1: Run the agent
        var runAgent = new AiAssistantActivity
        {
            AiAssistant = new Input<string>(agent),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Model = new Input<string>(model),
            ModelPower = new Input<int>(modelPower),
            Response = new Output<string>(loopOutput),
            Id = "loop-runner"
        };

        // Step 2: Classify the latest agent output to decide whether another pass is required.
        var classify = new TriageActivity
        {
            Prompt = new Input<string>(ctx =>
            {
                var originalPrompt = ctx.GetVariable<string>("Prompt") ?? "";
                var latestOutput = ctx.GetVariable<string>("LoopOutput") ?? "";
                return $$"""
                    Original task:
                    {{originalPrompt}}

                    Latest worker output:
                    {{latestOutput}}
                    """;
            }),
            ContainerId = new Input<string>(containerId),
            ClassificationInstructions = new Input<string?>(
                """
                Decide whether the latest worker output shows the task is complete.
                Return low complexity (1-4) when the task is complete and no more iterations are needed.
                Return high complexity (8-10) when another iteration is still required.
                Set category to "testing".
                """),
            Id = "loop-classifier"
        };

        var destroy = new DestroyContainerActivity
        {
            ContainerId = new Input<string>(ctx =>
                ctx.GetVariable<string>("ContainerId")
                ?? ctx.GetInput<string>("ContainerId")
                ?? ""),
            Id = "loop-destroy"
        };

        var flowchart = new Flowchart
        {
            Id = "loop-verifier-flow",
            Start = spawn,
            Activities = { spawn, iterationGate, runAgent, classify, destroy },
            Connections =
            {
                new Connection(
                    new Endpoint(spawn, "Done"),
                    new Endpoint(iterationGate)),
                new Connection(
                    new Endpoint(spawn, "Failed"),
                    new Endpoint(destroy)),

                new Connection(
                    new Endpoint(iterationGate, "Continue"),
                    new Endpoint(runAgent)),
                new Connection(
                    new Endpoint(iterationGate, "Exceeded"),
                    new Endpoint(destroy)),

                // Runner done -> classify
                new Connection(
                    new Endpoint(runAgent, "Done"),
                    new Endpoint(classify)),

                // Runner failed -> classify (check if partial progress)
                new Connection(
                    new Endpoint(runAgent, "Failed"),
                    new Endpoint(classify)),

                // Classifier says Simple (task complete) -> cleanup
                new Connection(
                    new Endpoint(classify, "Simple"),
                    new Endpoint(destroy)),

                // Classifier says Complex (not done yet) -> another bounded attempt
                new Connection(
                    new Endpoint(classify, "Complex"),
                    new Endpoint(iterationGate)),
            }
        };

        builder.Root = flowchart.WithAttachedVariables(builder);
    }
}
