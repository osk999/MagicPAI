using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Activities;
using Elsa.Workflows.Activities.Flowchart.Models;
using Elsa.Workflows.Models;
using Elsa.Workflows.Runtime.Activities;
using MagicPAI.Activities.Docker;

namespace MagicPAI.Server.Workflows;

/// <summary>
/// Standalone wrapper for the spawnless website-audit core.
/// </summary>
public class WebsiteAuditLoopWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        builder.Name = "Website Audit Loop";
        builder.Description =
            "Standalone website audit with container lifecycle and GUI-enabled browser execution";

        var containerId = builder.WithVariable<string>("ContainerId", "");

        var spawn = new SpawnContainerActivity
        {
            WorkspacePath = new Input<string>(""),
            EnableGui = new Input<bool>(ctx => ctx.GetInput<bool?>("EnableGui") ?? true),
            ContainerId = new Output<string>(containerId),
            Id = "audit-spawn"
        };

        var runAudit = new ExecuteWorkflow
        {
            WorkflowDefinitionId = new Input<string>(nameof(WebsiteAuditCoreWorkflow)),
            WaitForCompletion = new Input<bool>(true),
            Input = new Input<IDictionary<string, object>?>(ctx => new Dictionary<string, object>
            {
                ["Prompt"] = ctx.GetInput<string>("Prompt") ?? ctx.GetVariable<string>("Prompt") ?? "",
                ["AiAssistant"] = ctx.GetInput<string>("AiAssistant") ?? "claude",
                ["ContainerId"] = ctx.GetVariable<string>("ContainerId") ?? ""
            }),
            Id = "audit-core"
        };

        var destroy = new DestroyContainerActivity
        {
            ContainerId = new Input<string>(ctx =>
                ctx.GetVariable<string>("ContainerId")
                ?? ctx.GetInput<string>("ContainerId")
                ?? ""),
            Id = "audit-destroy"
        };

        var flowchart = new Flowchart
        {
            Id = "website-audit-loop-flow",
            Start = spawn,
            Activities = { spawn, runAudit, destroy },
            Connections =
            {
                new Connection(new Endpoint(spawn, "Done"), new Endpoint(runAudit)),
                new Connection(new Endpoint(spawn, "Failed"), new Endpoint(destroy)),
                new Connection(new Endpoint(runAudit, "Done"), new Endpoint(destroy)),
            }
        };

        builder.Root = flowchart.WithAttachedVariables(builder);
    }
}
