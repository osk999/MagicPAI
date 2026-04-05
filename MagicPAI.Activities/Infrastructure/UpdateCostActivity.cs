using System.Text.Json;
using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Attributes;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;

namespace MagicPAI.Activities.Infrastructure;

[Activity("MagicPAI", "Infrastructure", "Track and accumulate token costs for the workflow")]
[FlowNode("Done")]
public class UpdateCostActivity : Activity
{
    [Input(DisplayName = "Cost USD")]
    public Input<decimal> CostUsd { get; set; } = default!;

    [Input(DisplayName = "Agent")]
    public Input<string> Agent { get; set; } = new("claude");

    [Input(DisplayName = "Model")]
    public Input<string> Model { get; set; } = new("sonnet");

    [Input(DisplayName = "Input Tokens")]
    public Input<int> InputTokens { get; set; } = new(0);

    [Input(DisplayName = "Output Tokens")]
    public Input<int> OutputTokens { get; set; } = new(0);

    [Output(DisplayName = "Total Cost USD")]
    public Output<decimal> TotalCostUsd { get; set; } = default!;

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var cost = CostUsd.Get(context);
        var agent = Agent.Get(context);
        var model = Model.Get(context);

        context.AddExecutionLogEntry("CostUpdate",
            JsonSerializer.Serialize(new
            {
                agent,
                model,
                costUsd = cost,
                inputTokens = InputTokens.Get(context),
                outputTokens = OutputTokens.Get(context),
                timestamp = DateTime.UtcNow
            }));

        // The total cost is typically tracked via a workflow variable.
        // This activity just records the increment; the caller aggregates.
        TotalCostUsd.Set(context, cost);

        await context.CompleteActivityWithOutcomesAsync("Done");
    }
}
