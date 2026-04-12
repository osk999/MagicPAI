using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Activities;
using Elsa.Workflows.Activities.Flowchart.Models;
using Elsa.Workflows.Models;
using Elsa.Workflows.Runtime.Activities;
using MagicPAI.Activities.AI;
using MagicPAI.Activities.Docker;

namespace MagicPAI.Server.Workflows;

/// <summary>
/// Parent orchestration workflow that owns the container lifecycle and delegates
/// specialized execution to reusable child workflows.
/// </summary>
public class FullOrchestrateWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        builder.Name = "Full Orchestrate";
        builder.Description =
            "Complete AI orchestration: website routing, triage, child workflow execution, and cleanup";

        var prompt = builder.WithVariable<string>("Prompt", "");
        var containerId = builder.WithVariable<string>("ContainerId", "");
        var assistant = builder.WithVariable<string>("AiAssistant", "");
        var model = builder.WithVariable<string>("Model", "");
        var modelPower = builder.WithVariable<int>("ModelPower", 0);
        var recommendedModel = builder.WithVariable<string>("RecommendedModel", "");

        Input<string> resolveContainerId() => new(ctx =>
            ctx.GetVariable<string>("ContainerId")
            ?? ctx.GetInput<string>("ContainerId")
            ?? "");

        Input<IDictionary<string, object>?> buildChildInput() => new(ctx =>
        {
            var resolvedAssistant =
                ctx.GetInput<string>("AiAssistant")
                ?? ctx.GetInput<string>("Agent")
                ?? ctx.GetVariable<string>("AiAssistant")
                ?? "";
            var requestedModel = ctx.GetInput<string>("Model");
            var resolvedModel =
                string.IsNullOrWhiteSpace(requestedModel) ||
                string.Equals(requestedModel, "auto", StringComparison.OrdinalIgnoreCase)
                    ? ctx.GetVariable<string>("RecommendedModel")
                        ?? ctx.GetVariable<string>("Model")
                        ?? ""
                    : requestedModel;

            return new Dictionary<string, object>
            {
                ["Prompt"] = ctx.GetInput<string>("Prompt") ?? ctx.GetVariable<string>("Prompt") ?? "",
                ["AiAssistant"] = resolvedAssistant,
                ["Agent"] = resolvedAssistant,
                ["Model"] = resolvedModel,
                ["ModelPower"] = ctx.GetVariable<int>("ModelPower"),
                ["ContainerId"] = ctx.GetVariable<string>("ContainerId") ?? ctx.GetInput<string>("ContainerId") ?? "",
                ["EnableGui"] = ctx.GetInput<bool?>("EnableGui") ?? true
            };
        });

        var spawn = new SpawnContainerActivity
        {
            WorkspacePath = new Input<string>(""),
            EnableGui = new Input<bool>(ctx => ctx.GetInput<bool?>("EnableGui") ?? true),
            ContainerId = new Output<string>(containerId),
            Id = "spawn-container"
        };
        Pos(spawn, 400, 50);

        var websiteClassifier = new WebsiteTaskClassifierActivity
        {
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Id = "website-classifier"
        };
        Pos(websiteClassifier, 400, 220);

        var triage = new TriageActivity
        {
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            RecommendedModel = new Output<string>(recommendedModel),
            RecommendedModelPower = new Output<int>(modelPower),
            Id = "triage"
        };
        Pos(triage, 400, 400);

        var simplePath = new ExecuteWorkflow
        {
            WorkflowDefinitionId = new Input<string>(nameof(OrchestrateSimplePathWorkflow)),
            WaitForCompletion = new Input<bool>(true),
            Input = buildChildInput(),
            Id = "simple-path"
        };
        Pos(simplePath, 250, 560);

        var complexPath = new ExecuteWorkflow
        {
            WorkflowDefinitionId = new Input<string>(nameof(OrchestrateComplexPathWorkflow)),
            WaitForCompletion = new Input<bool>(true),
            Input = buildChildInput(),
            Id = "complex-path"
        };
        Pos(complexPath, 550, 560);

        var websiteAudit = new ExecuteWorkflow
        {
            WorkflowDefinitionId = new Input<string>(nameof(WebsiteAuditCoreWorkflow)),
            WaitForCompletion = new Input<bool>(true),
            Input = buildChildInput(),
            Id = "website-audit"
        };
        Pos(websiteAudit, 400, 720);

        var destroy = new DestroyContainerActivity
        {
            ContainerId = resolveContainerId(),
            Id = "destroy-container"
        };
        Pos(destroy, 400, 880);

        var flowchart = new Flowchart
        {
            Id = "full-orchestrate-flow",
            Start = spawn,
            Activities = { spawn, websiteClassifier, triage, simplePath, complexPath, websiteAudit, destroy },
            Connections =
            {
                new Connection(new Endpoint(spawn, "Done"), new Endpoint(websiteClassifier)),
                new Connection(new Endpoint(spawn, "Failed"), new Endpoint(destroy)),

                // Both Website and NonWebsite converge on Triage
                new Connection(new Endpoint(websiteClassifier, "Website"), new Endpoint(triage)),
                new Connection(new Endpoint(websiteClassifier, "NonWebsite"), new Endpoint(triage)),

                new Connection(new Endpoint(triage, "Simple"), new Endpoint(simplePath)),
                new Connection(new Endpoint(triage, "Complex"), new Endpoint(complexPath)),

                // After code execution, always run website audit
                new Connection(new Endpoint(simplePath, "Done"), new Endpoint(websiteAudit)),
                new Connection(new Endpoint(complexPath, "Done"), new Endpoint(websiteAudit)),

                new Connection(new Endpoint(websiteAudit, "Done"), new Endpoint(destroy)),
            }
        };

        builder.Root = flowchart.WithAttachedVariables(builder);
    }
}
