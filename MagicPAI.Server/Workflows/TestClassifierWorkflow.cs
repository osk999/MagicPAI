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
/// Test workflow that exercises the new ClassifierActivity through Elsa runtime.
/// Spawn container -> Classify("Is this complex?") -> Destroy container.
/// </summary>
public class TestClassifierWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        var containerIdVar = new Variable<string>("ContainerId", "");

        builder.Name = "Test Classifier";
        builder.Description = "Test ClassifierActivity with real prompt through Elsa";
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
                "Redesign the authentication system with OAuth2, SAML, and MFA support across 15 microservices"),
            ClassificationQuestion = new Input<string>(
                "Does this task require multi-file changes, architectural decisions, or multi-step implementation?"),
            ContainerId = new Input<string>(containerIdVar),
            ModelPower = new Input<int>(1),
            Id = "test-classifier"
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
            Id = "test-classifier-flow",
            Activities = { spawn, classifier, destroy },
            Connections =
            {
                new Connection(
                    new Endpoint(spawn, "Done"),
                    new Endpoint(classifier)),
                new Connection(
                    new Endpoint(classifier, "True"),
                    new Endpoint(destroy)),
                new Connection(
                    new Endpoint(classifier, "False"),
                    new Endpoint(destroy))
            }
        };
    }
}
