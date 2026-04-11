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
/// Test: PromptEnhancementActivity with a vague prompt.
/// Spawn -> Enhance("make it work") -> Destroy.
/// </summary>
public class TestPromptEnhancementWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        var containerIdVar = new Variable<string>("ContainerId", "");
        builder.Name = "Test Prompt Enhancement";
        builder.Description = "Test PromptEnhancementActivity with a vague prompt";
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

        var enhance = new PromptEnhancementActivity
        {
            OriginalPrompt = new Input<string>("make it work"),
            EnhancementInstructions = new Input<string>(""),
            ContainerId = new Input<string>(containerIdVar),
            ModelPower = new Input<int>(3),
            Id = "test-enhancer"
        };

        var destroy = new DestroyContainerActivity
        {
            ContainerId = new Input<string>(containerIdVar),
            Id = "destroy-container"
        };

        builder.Root = new Flowchart
        {
            Id = "test-enhancement-flow",
            Activities = { spawn, enhance, destroy },
            Connections =
            {
                new Connection(new Endpoint(spawn, "Done"), new Endpoint(enhance)),
                new Connection(new Endpoint(enhance, "Done"), new Endpoint(destroy)),
                new Connection(new Endpoint(enhance, "Failed"), new Endpoint(destroy))
            }
        };
    }
}
