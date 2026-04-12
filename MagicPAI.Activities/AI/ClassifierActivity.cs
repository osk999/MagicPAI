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
/// Generic binary classifier using AI. Sends a yes/no question about the prompt
/// to the AI, parses the structured response, and branches on True/False.
/// Replaces TriageActivity and WebsiteTaskClassifierActivity.
/// </summary>
[Activity("MagicPAI", "AI Agents", "Binary classifier using AI — answers a yes/no question about the input")]
[FlowNode("True", "False")]
public class ClassifierActivity : Activity
{
    [Input(DisplayName = "Prompt", UIHint = InputUIHints.MultiLine,
        Description = "The content to classify")]
    public Input<string> Prompt { get; set; } = new("");

    [Input(DisplayName = "Classification Question", UIHint = InputUIHints.MultiLine,
        Description = "The yes/no question to ask about the prompt (e.g. 'Is this a complex multi-file task?')")]
    public Input<string> ClassificationQuestion { get; set; } = new("");

    [Input(DisplayName = "Container ID")]
    public Input<string> ContainerId { get; set; } = new("");

    [Input(DisplayName = "Model Power", Category = "Model",
        Description = "1 = strongest (opus-class), 2 = balanced (sonnet-class)")]
    public Input<int> ModelPower { get; set; } = new(1);

    [Output(DisplayName = "Result")]
    public Output<bool> Result { get; set; } = default!;

    [Output(DisplayName = "Confidence")]
    public Output<int> Confidence { get; set; } = default!;

    [Output(DisplayName = "Rationale")]
    public Output<string> Rationale { get; set; } = default!;

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        try
        {
            var prompt = ActivityHelpers.GetOrDefault(Prompt, context, "");
            if (string.IsNullOrWhiteSpace(prompt))
                prompt = context.GetOptionalWorkflowInput<string>("Prompt") ?? "";

            var question = ActivityHelpers.GetOrDefault(ClassificationQuestion, context, "");

            var classifyPrompt = BuildClassificationPrompt(prompt, question);
            var schema = SchemaGenerator.FromType<ClassificationResult>();

            var result = await AiCliExecutor.ExecuteAsync(context,
                new AiCliExecutor.ExecutionParams
                {
                    ContainerId = ActivityHelpers.ResolveContainerId(ContainerId, context),
                    Prompt = classifyPrompt,
                    ModelPower = ActivityHelpers.GetOrDefault(ModelPower, context, 1),
                    OutputSchema = schema,
                    UseStreaming = false,
                    MaxRetries = 2,
                    TimeoutMinutes = 5
                });

            var parsed = ParseClassificationResult(
                result.StructuredOutputJson ?? result.Response);

            Result.Set(context, parsed.Result);
            Confidence.Set(context, parsed.Confidence);
            Rationale.Set(context, parsed.Rationale);

            context.AddExecutionLogEntry("ClassificationResult",
                JsonSerializer.Serialize(new
                {
                    question,
                    result = parsed.Result,
                    confidence = parsed.Confidence,
                    rationale = parsed.Rationale
                }));

            await context.CompleteActivityWithOutcomesAsync(
                parsed.Result ? "True" : "False");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            context.AddExecutionLogEntry("ClassificationFailed", ex.ToString());
            Result.Set(context, false);
            Confidence.Set(context, 0);
            Rationale.Set(context, $"Classification error: {ex.Message}");
            await context.CompleteActivityWithOutcomesAsync("False");
        }
    }

    internal static string BuildClassificationPrompt(string content, string question) =>
        $$"""
        You are a classifier. Answer the following yes/no question about the task below.

        Question: {{question}}

        Respond with JSON only:
        {
          "result": <true|false>,
          "confidence": <1-10>,
          "rationale": "<short explanation>"
        }

        Task: {{content}}
        """;

    internal static ClassificationResult ParseClassificationResult(string output)
    {
        try
        {
            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;
            return new ClassificationResult(
                Result: root.TryGetProperty("result", out var r) && r.GetBoolean(),
                Confidence: root.TryGetProperty("confidence", out var c) ? c.GetInt32() : 5,
                Rationale: root.TryGetProperty("rationale", out var rat)
                    ? rat.GetString() ?? "" : "");
        }
        catch
        {
            return new ClassificationResult(false, 0, "Failed to parse classification.");
        }
    }
}
