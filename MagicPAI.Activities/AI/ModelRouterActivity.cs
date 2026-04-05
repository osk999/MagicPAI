using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Attributes;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;
using Elsa.Workflows.UIHints;

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
        var complexity = Complexity.Get(context);
        var agent = PreferredAgent.Get(context);

        var model = (agent, complexity) switch
        {
            ("claude", <= 3) => "haiku",
            ("claude", <= 7) => "sonnet",
            ("claude", _) => "opus",
            ("codex", <= 5) => "gpt-5.4-mini",
            ("codex", _) => "gpt-5.4",
            ("gemini", <= 5) => "gemini-3-flash",
            ("gemini", _) => "gemini-3.1-pro-preview",
            _ => "sonnet"
        };

        SelectedAgent.Set(context, agent);
        SelectedModel.Set(context, model);

        context.AddExecutionLogEntry("ModelRouting",
            $"Selected {agent}/{model} for complexity {complexity}");

        await context.CompleteActivityWithOutcomesAsync("Done");
    }
}
