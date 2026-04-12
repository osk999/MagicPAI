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

/// <summary>
/// Enhance a prompt using AI. Sends the original prompt to the AI with enhancement
/// instructions and a structured output schema, then returns the improved prompt.
/// Falls back to the original prompt on failure.
/// </summary>
[Activity("MagicPAI", "AI Agents", "Enhance a prompt using AI")]
[FlowNode("Done", "Failed")]
public class PromptEnhancementActivity : Activity
{
    [Input(DisplayName = "Original Prompt", UIHint = InputUIHints.MultiLine)]
    public Input<string> OriginalPrompt { get; set; } = new("");

    [Input(DisplayName = "Enhancement Instructions", UIHint = InputUIHints.MultiLine,
        Description = "Optional: specific instructions for how to enhance the prompt",
        Category = "Enhancement")]
    public Input<string> EnhancementInstructions { get; set; } = new("");

    [Input(DisplayName = "Container ID")]
    public Input<string> ContainerId { get; set; } = new("");

    [Input(DisplayName = "Model Power", Category = "Model",
        Description = "1 = strongest (opus-class), 2 = balanced (sonnet-class)")]
    public Input<int> ModelPower { get; set; } = new(1);

    [Output(DisplayName = "Enhanced Prompt")]
    public Output<string> EnhancedPrompt { get; set; } = default!;

    [Output(DisplayName = "Was Enhanced")]
    public Output<bool> WasEnhanced { get; set; } = default!;

    [Output(DisplayName = "Rationale")]
    public Output<string> Rationale { get; set; } = default!;

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var original = ActivityHelpers.GetOrDefault(OriginalPrompt, context, "");
        if (string.IsNullOrWhiteSpace(original))
            original = context.GetOptionalWorkflowInput<string>("Prompt") ?? "";

        try
        {
            var instructions = ActivityHelpers.GetOrDefault(
                EnhancementInstructions, context, "");

            var enhancePrompt = BuildEnhancementPrompt(original, instructions);
            var schema = SchemaGenerator.FromType<PromptEnhancementResult>();

            var result = await AiCliExecutor.ExecuteAsync(context,
                new AiCliExecutor.ExecutionParams
                {
                    ContainerId = ActivityHelpers.ResolveContainerId(ContainerId, context),
                    Prompt = enhancePrompt,
                    ModelPower = ActivityHelpers.GetOrDefault(ModelPower, context, 1),
                    OutputSchema = schema,
                    UseStreaming = false,
                    MaxRetries = 2,
                    TimeoutMinutes = 10
                });

            if (!result.Success)
            {
                EnhancedPrompt.Set(context, original);
                WasEnhanced.Set(context, false);
                Rationale.Set(context, "Enhancement failed, using original prompt.");
                await context.CompleteActivityWithOutcomesAsync("Failed");
                return;
            }

            var parsed = ParseEnhancementResult(
                result.StructuredOutputJson ?? result.Response, original);

            context.AddExecutionLogEntry("PromptEnhancement",
                JsonSerializer.Serialize(new
                {
                    wasEnhanced = parsed.WasEnhanced,
                    rationale = parsed.Rationale,
                    originalLength = original.Length,
                    enhancedLength = parsed.EnhancedPrompt.Length
                }));

            EnhancedPrompt.Set(context, parsed.EnhancedPrompt);
            WasEnhanced.Set(context, parsed.WasEnhanced);
            Rationale.Set(context, parsed.Rationale);

            await context.CompleteActivityWithOutcomesAsync("Done");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            context.AddExecutionLogEntry("PromptEnhancementFailed", ex.ToString());
            EnhancedPrompt.Set(context, original);
            WasEnhanced.Set(context, false);
            Rationale.Set(context, ex.Message);
            await context.CompleteActivityWithOutcomesAsync("Failed");
        }
    }

    internal static string BuildEnhancementPrompt(string original, string instructions)
    {
        var instructionBlock = string.IsNullOrWhiteSpace(instructions)
            ? "You are a prompt engineer. Improve this prompt for a coding AI agent."
            : instructions.Trim();

        return $$"""
            {{instructionBlock}}

            Make the prompt more specific, actionable, and unambiguous.
            If the prompt is already clear and specific, return it unchanged and set was_enhanced to false.

            Respond with JSON only:
            {
              "enhanced_prompt": "<the improved prompt text>",
              "was_enhanced": <true|false>,
              "rationale": "<why you made changes or why no changes needed>"
            }

            Original prompt: {{original}}
            """;
    }

    internal static PromptEnhancementResult ParseEnhancementResult(
        string output, string fallbackPrompt)
    {
        try
        {
            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;
            return new PromptEnhancementResult(
                EnhancedPrompt: root.TryGetProperty("enhanced_prompt", out var ep)
                    ? ep.GetString() ?? fallbackPrompt : fallbackPrompt,
                WasEnhanced: root.TryGetProperty("was_enhanced", out var we) && we.GetBoolean(),
                Rationale: root.TryGetProperty("rationale", out var r)
                    ? r.GetString() ?? "" : "");
        }
        catch
        {
            return new PromptEnhancementResult(fallbackPrompt, false,
                "Failed to parse enhancement result.");
        }
    }
}
