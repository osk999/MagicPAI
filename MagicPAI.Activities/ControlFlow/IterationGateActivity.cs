using System.Text.Json;
using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Attributes;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;

namespace MagicPAI.Activities.ControlFlow;

[Activity("MagicPAI", "Control Flow", "Increment a loop counter and stop when the iteration limit is reached")]
[FlowNode("Continue", "Exceeded")]
public class IterationGateActivity : Activity
{
    [Input(DisplayName = "Current Count")]
    public Input<int> CurrentCount { get; set; } = new(0);

    [Input(DisplayName = "Max Iterations")]
    public Input<int> MaxIterations { get; set; } = new(1);

    [Input(DisplayName = "Label")]
    public Input<string> Label { get; set; } = new("Iteration");

    [Output(DisplayName = "Next Count")]
    public Output<int> NextCount { get; set; } = default!;

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var current = GetOrDefault(CurrentCount, context, 0);
        var max = Math.Max(1, GetOrDefault(MaxIterations, context, 1));
        var next = current + 1;
        var exceeded = next > max;

        NextCount.Set(context, next);
        context.AddExecutionLogEntry("IterationGate",
            JsonSerializer.Serialize(new
            {
                label = GetOrDefault(Label, context, "Iteration"),
                current,
                next,
                max,
                exceeded
            }));

        await context.CompleteActivityWithOutcomesAsync(exceeded ? "Exceeded" : "Continue");
    }

    private static T GetOrDefault<T>(Input<T>? input, ActivityExecutionContext context, T fallback)
    {
        if (input is null)
            return fallback;

        try
        {
            return input.Get(context);
        }
        catch (InvalidOperationException)
        {
            return fallback;
        }
    }
}
