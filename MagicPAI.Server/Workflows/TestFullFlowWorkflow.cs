using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Activities.Flowchart.Activities;
using Elsa.Workflows.Activities.Flowchart.Models;
using Elsa.Workflows.Memory;
using Elsa.Workflows.Models;
using MagicPAI.Activities.AI;
using MagicPAI.Activities.Docker;

namespace MagicPAI.Server.Workflows;

/// <summary>
/// Test: Full flow — Classify complexity -> If simple, run agent directly.
/// If complex, enhance prompt first then run agent.
/// Spawn -> Classify -> [True: Enhance -> Agent] / [False: Agent] -> Destroy.
/// </summary>
public class TestFullFlowWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        var containerIdVar = new Variable<string>("ContainerId", "");
        builder.Name = "Test Full Flow";
        builder.Description = "Test full orchestration: classify -> enhance -> execute";
        builder.WithVariable(containerIdVar);

        var spawn = new SpawnContainerActivity
        {
            Image = new Input<string>("magicpai-env:latest"),
            WorkspacePath = new Input<string>(Path.Combine(Path.GetTempPath(), "magicpai-test")),
            MemoryLimitMb = new Input<int>(2048),
            EnableGui = new Input<bool>(false),
            ContainerId = new Output<string>(containerIdVar),
            Id = "spawn-container"
        };

        // Step 1: Is this complex?
        var classify = new ClassifierActivity
        {
            Prompt = new Input<string>(
                "Add input validation to the user registration form — check email format and password strength"),
            ClassificationQuestion = new Input<string>(
                "Does this task require multi-file changes, architectural decisions, or multi-step implementation?"),
            ContainerId = new Input<string>(containerIdVar),
            ModelPower = new Input<int>(3),
            Id = "classify-complexity"
        };

        // Step 2a (complex path): Enhance the prompt
        var enhance = new PromptEnhancementActivity
        {
            OriginalPrompt = new Input<string>(
                "Add input validation to the user registration form — check email format and password strength"),
            ContainerId = new Input<string>(containerIdVar),
            ModelPower = new Input<int>(3),
            Id = "enhance-prompt"
        };

        // Step 2b: Run the agent (simple path skips enhancement)
        var agent = new RunCliAgentActivity
        {
            AiAssistant = new Input<string>("claude"),
            Prompt = new Input<string>(
                "List 3 specific validation rules for a user registration form with email and password fields. Just list them, no code."),
            ContainerId = new Input<string>(containerIdVar),
            Model = new Input<string>(""),
            ModelPower = new Input<int>(3),
            MaxTurns = new Input<int>(5),
            TimeoutMinutes = new Input<int>(5),
            Id = "run-agent"
        };

        var destroy = new DestroyContainerActivity
        {
            ContainerId = new Input<string>(containerIdVar),
            Id = "destroy-container"
        };

        builder.Root = new Flowchart
        {
            Id = "test-full-flow",
            Activities = { spawn, classify, enhance, agent, destroy },
            Connections =
            {
                new Connection(new Endpoint(spawn, "Done"), new Endpoint(classify)),
                // Complex: classify -> enhance -> agent
                new Connection(new Endpoint(classify, "True"), new Endpoint(enhance)),
                new Connection(new Endpoint(enhance, "Done"), new Endpoint(agent)),
                new Connection(new Endpoint(enhance, "Failed"), new Endpoint(agent)),
                // Simple: classify -> agent directly
                new Connection(new Endpoint(classify, "False"), new Endpoint(agent)),
                // Agent -> destroy
                new Connection(new Endpoint(agent, "Done"), new Endpoint(destroy)),
                new Connection(new Endpoint(agent, "Failed"), new Endpoint(destroy))
            }
        };
    }
}
