using System.Text.Json;
using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Attributes;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;
using Elsa.Workflows.UIHints;

namespace MagicPAI.Activities.Infrastructure;

[Activity("MagicPAI", "Infrastructure",
    "Emit an output chunk for the SignalR bridge to stream to clients")]
[FlowNode("Done")]
public class EmitOutputChunkActivity : Activity
{
    [Input(DisplayName = "Text", UIHint = InputUIHints.MultiLine)]
    public Input<string> Text { get; set; } = default!;

    [Input(DisplayName = "Source",
        Description = "Identifier for the source of this output (e.g. activity ID, agent name)")]
    public Input<string> Source { get; set; } = new("activity");

    [Input(DisplayName = "Level",
        UIHint = InputUIHints.DropDown,
        Options = new[] { "info", "warning", "error", "debug" })]
    public Input<string> Level { get; set; } = new("info");

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var text = Text.Get(context);
        var source = Source.Get(context);
        var level = Level.Get(context);

        context.AddExecutionLogEntry("OutputChunk",
            JsonSerializer.Serialize(new
            {
                activityId = context.Activity.Id,
                source,
                level,
                text,
                timestamp = DateTime.UtcNow
            }));

        await context.CompleteActivityWithOutcomesAsync("Done");
    }
}
