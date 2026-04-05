using System.Text.Json;
using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Attributes;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;
using Elsa.Workflows.UIHints;
using MagicPAI.Core.Models;
using MagicPAI.Core.Services;

namespace MagicPAI.Activities.AI;

[Activity("MagicPAI", "AI Agents", "Classify prompt complexity using a cheap model")]
[FlowNode("Simple", "Complex")]
public class TriageActivity : Activity
{
    [Input(DisplayName = "Prompt", UIHint = InputUIHints.MultiLine)]
    public Input<string> Prompt { get; set; } = default!;

    [Input(DisplayName = "Container ID")]
    public Input<string> ContainerId { get; set; } = default!;

    [Output(DisplayName = "Complexity")]
    public Output<int> Complexity { get; set; } = default!;

    [Output(DisplayName = "Category")]
    public Output<string> Category { get; set; } = default!;

    [Output(DisplayName = "Recommended Model")]
    public Output<string> RecommendedModel { get; set; } = default!;

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var containerMgr = context.GetRequiredService<IContainerManager>();
        var agentFactory = context.GetRequiredService<ICliAgentFactory>();

        var runner = agentFactory.Create("claude");
        var triagePrompt = BuildTriagePrompt(Prompt.Get(context));
        var command = runner.BuildCommand(triagePrompt, "haiku", 1, "/workspace");

        var result = await containerMgr.ExecAsync(
            ContainerId.Get(context), command, "/workspace", context.CancellationToken);

        var parsed = ParseTriageResponse(result.Output);
        Complexity.Set(context, parsed.Complexity);
        Category.Set(context, parsed.Category);
        RecommendedModel.Set(context, parsed.RecommendedModel);

        context.AddExecutionLogEntry("TriageResult",
            $"Complexity={parsed.Complexity}, Category={parsed.Category}");

        var outcome = parsed.Complexity >= 7 ? "Complex" : "Simple";
        await context.CompleteActivityWithOutcomesAsync(outcome);
    }

    private static string BuildTriagePrompt(string userPrompt) =>
        $$"""
        Analyze this coding task and respond with JSON only:
        {
          "complexity": <1-10>,
          "category": "<code_gen|bug_fix|refactor|architecture|testing|docs>",
          "needs_decomposition": <true|false>,
          "recommended_model": "<haiku|sonnet|opus>",
          "estimated_tasks": <number if decomposition needed>,
          "reasoning": "<brief explanation>"
        }

        Task: {{userPrompt}}
        """;

    private static TriageResult ParseTriageResponse(string output)
    {
        try
        {
            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;
            return new TriageResult(
                Complexity: root.TryGetProperty("complexity", out var c) ? c.GetInt32() : 5,
                Category: root.TryGetProperty("category", out var cat) ? cat.GetString() ?? "code_gen" : "code_gen",
                RecommendedModel: root.TryGetProperty("recommended_model", out var m) ? m.GetString() ?? "sonnet" : "sonnet",
                NeedsDecomposition: root.TryGetProperty("needs_decomposition", out var nd) && nd.GetBoolean());
        }
        catch
        {
            return new TriageResult(5, "code_gen", "sonnet", false);
        }
    }
}
