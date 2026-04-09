using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Attributes;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;
using Elsa.Workflows.UIHints;
using MagicPAI.Activities;
using MagicPAI.Core.Config;
using MagicPAI.Core.Services;

namespace MagicPAI.Activities.AI;

[Activity("MagicPAI", "AI Agents", "Select the best model based on task category and complexity")]
[FlowNode("Done")]
public class ModelRouterActivity : Activity
{
    [Input(DisplayName = "Task Category")]
    public Input<string> TaskCategory { get; set; } = default!;

    [Input(DisplayName = "Complexity (1-10)")]
    public Input<int> Complexity { get; set; } = default!;

    [Input(DisplayName = "Preferred Agent",
        UIHint = InputUIHints.DropDown,
        Options = new[] { "claude", "codex", "gemini" })]
    public Input<string> PreferredAgent { get; set; } = new("claude");

    [Output(DisplayName = "Selected Agent")]
    public Output<string> SelectedAgent { get; set; } = default!;

    [Output(DisplayName = "Selected Model")]
    public Output<string> SelectedModel { get; set; } = default!;

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var agentFactory = context.GetRequiredService<ICliAgentFactory>();
        var config = context.GetRequiredService<MagicPaiConfig>();
        var complexity = Complexity.Get(context);
        var agent = AiAssistantResolver.NormalizeAssistant(
            PreferredAgent.Get(context),
            config.DefaultAgent);
        var runner = agentFactory.Create(agent);
        var requestedModel = context.GetOptionalWorkflowInput<string>("Model");
        var requestedModelPower = context.GetOptionalWorkflowInput<int?>("ModelPower");
        var resolved = !string.IsNullOrWhiteSpace(requestedModel) ||
                       requestedModelPower.GetValueOrDefault() > 0
            ? AiAssistantResolver.Resolve(runner, config, agent, requestedModel, requestedModelPower)
            : new ResolvedAssistantOptions(
                agent,
                AiAssistantResolver.ResolveModelForPower(
                    runner,
                    config,
                    AiAssistantResolver.GetRecommendedModelPower(complexity)),
                AiAssistantResolver.GetRecommendedModelPower(complexity));

        SelectedAgent.Set(context, agent);
        SelectedModel.Set(context, resolved.Model ?? runner.DefaultModel);

        context.AddExecutionLogEntry("ModelRouting",
            $"Selected {agent}/{resolved.Model ?? runner.DefaultModel} (power={resolved.ModelPower ?? 0}) for complexity {complexity}");

        await context.CompleteActivityWithOutcomesAsync("Done");
    }
}
