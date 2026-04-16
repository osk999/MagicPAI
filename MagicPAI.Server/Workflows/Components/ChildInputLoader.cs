using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities;
using MagicPAI.Core.Services;
using System.Text.Json;

namespace MagicPAI.Server.Workflows.Components;

/// <summary>
/// Factory for the "init-vars" inline activity that child workflows use to copy
/// input from the parent into their own variables. Elsa's
/// DispatchWorkflow/ExecuteWorkflow does NOT reliably propagate the Input
/// dictionary into a dispatched child's WorkflowInput / workflow-scoped variables,
/// so the parent workflow puts the payload into SharedBlackboard keyed by its own
/// instance id + ":child-input", and every child calls this helper at the start
/// of its flowchart to hydrate the variables (ContainerId, Prompt, AiAssistant,
/// Model, ModelPower). Without this, children spin up with empty ContainerId and
/// every AI activity throws "Container ID is required".
/// </summary>
public static class ChildInputLoader
{
    public static Inline Build(string id = "init-vars")
    {
        var inline = new Inline(ctx =>
        {
            var bb = ctx.GetRequiredService<SharedBlackboard>();
            var parentId = ctx.WorkflowExecutionContext.Properties.TryGetValue("ParentInstanceId", out var pid)
                ? pid?.ToString()
                : null;
            if (string.IsNullOrWhiteSpace(parentId))
                parentId = ctx.WorkflowExecutionContext.ParentWorkflowInstanceId;

            var stored = !string.IsNullOrWhiteSpace(parentId)
                ? bb.GetTaskOutput($"{parentId}:child-input")
                : null;

            if (string.IsNullOrWhiteSpace(stored))
                return;

            Dictionary<string, JsonElement>? data = null;
            try
            {
                data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(stored);
            }
            catch
            {
                return;
            }

            if (data is null) return;

            void SetString(string variableName, params string[] keys)
            {
                foreach (var key in keys)
                {
                    if (data.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String)
                    {
                        var s = el.GetString();
                        if (!string.IsNullOrWhiteSpace(s))
                        {
                            ctx.SetVariable(variableName, s);
                            return;
                        }
                    }
                }
            }

            SetString("Prompt", "Prompt");
            SetString("ContainerId", "ContainerId");
            SetString("AiAssistant", "AiAssistant", "Agent");
            SetString("Model", "Model");

            if (data.TryGetValue("ModelPower", out var mp) && mp.ValueKind == JsonValueKind.Number)
                ctx.SetVariable("ModelPower", mp.GetInt32());
        });
        inline.Id = id;
        return inline;
    }
}
