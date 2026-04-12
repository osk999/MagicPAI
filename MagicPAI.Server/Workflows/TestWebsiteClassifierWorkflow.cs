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
/// Test: ClassifierActivity for website task detection.
/// Spawn -> Classify("Is this a website task?") -> Destroy.
/// </summary>
public class TestWebsiteClassifierWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        var containerIdVar = new Variable<string>("ContainerId", "");
        builder.Name = "Test Website Classifier";
        builder.Description = "Test ClassifierActivity for website routing";
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
        Pos(spawn, 400, 50);

        var classifier = new ClassifierActivity
        {
            Prompt = new Input<string>(
                "Audit the marketing website at example.com for accessibility issues, check mobile responsiveness and WCAG 2.1 AA compliance"),
            ClassificationQuestion = new Input<string>(
                "Does this task involve browser-based website auditing, UI/UX review, frontend interaction, layout analysis, or accessibility review?"),
            ContainerId = new Input<string>(containerIdVar),
            ModelPower = new Input<int>(1),
            Id = "test-website-classifier"
        };
        Pos(classifier, 400, 220);

        var destroy = new DestroyContainerActivity
        {
            ContainerId = new Input<string>(containerIdVar),
            Id = "destroy-container"
        };
        Pos(destroy, 400, 390);

        builder.Root = new Flowchart
        {
            Id = "test-website-classifier-flow",
            Activities = { spawn, classifier, destroy },
            Connections =
            {
                new Connection(new Endpoint(spawn, "Done"), new Endpoint(classifier)),
                new Connection(new Endpoint(classifier, "True"), new Endpoint(destroy)),
                new Connection(new Endpoint(classifier, "False"), new Endpoint(destroy))
            }
        };
    }
}
