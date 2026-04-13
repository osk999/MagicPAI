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
/// MagicPrompt-style research component that enhances a prompt, grounds it in the
/// repository, and returns one implementation-ready prompt for downstream execution.
/// </summary>
[Activity("MagicPAI", "AI Agents", "Research-first prompt enhancement and grounding",
    Kind = ActivityKind.Task,
    RunAsynchronously = true)]
[FlowNode("Done", "Failed")]
public class ResearchPromptActivity : Activity
{
    [Input(DisplayName = "Prompt", UIHint = InputUIHints.MultiLine)]
    public Input<string> Prompt { get; set; } = new("");

    [Input(DisplayName = "AI Assistant",
        UIHint = InputUIHints.DropDown,
        Options = new[] { "claude", "codex", "gemini" },
        Description = "Which AI assistant should perform the research passes")]
    public Input<string> AiAssistant { get; set; } = new("");

    [Input(DisplayName = "Container ID")]
    public Input<string> ContainerId { get; set; } = new("");

    [Input(DisplayName = "Model Power",
        Description = "1 = strongest, 2 = balanced, 3 = fastest",
        Category = "Model")]
    public Input<int> ModelPower { get; set; } = new(2);

    [Output(DisplayName = "Enhanced Prompt")]
    public Output<string> EnhancedPrompt { get; set; } = default!;

    [Output(DisplayName = "Codebase Analysis")]
    public Output<string> CodebaseAnalysis { get; set; } = default!;

    [Output(DisplayName = "Research Context")]
    public Output<string> ResearchContext { get; set; } = default!;

    [Output(DisplayName = "Rationale")]
    public Output<string> Rationale { get; set; } = default!;

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var originalPrompt = ActivityHelpers.ResolvePrompt(Prompt, context);
        var assistant = ActivityHelpers.FirstNonEmpty(
            ActivityHelpers.Optional(AiAssistant, context),
            context.GetOptionalWorkflowInput<string>("AiAssistant"),
            context.GetOptionalWorkflowInput<string>("Agent"),
            "claude")!;

        if (string.IsNullOrWhiteSpace(originalPrompt))
        {
            EnhancedPrompt.Set(context, "");
            CodebaseAnalysis.Set(context, "");
            ResearchContext.Set(context, "");
            Rationale.Set(context, "Prompt was empty.");
            await context.CompleteActivityWithOutcomesAsync("Failed");
            return;
        }

        try
        {
            var containerId = ActivityHelpers.ResolveContainerId(ContainerId, context);
            var modelPower = ActivityHelpers.GetOrDefault(ModelPower, context, 2);

            var enhanced = await EnhancePromptAsync(
                context, originalPrompt, containerId, assistant, modelPower);

            var codebaseAnalysis = await ExecuteTextStepAsync(
                context,
                "ResearchPromptCodebaseAnalysis",
                BuildCodebaseAnalysisPrompt(originalPrompt, enhanced.EnhancedPrompt),
                containerId,
                assistant,
                modelPower);

            var researchContext = await ExecuteTextStepAsync(
                context,
                "ResearchPromptRepoMap",
                BuildResearchContextPrompt(originalPrompt, enhanced.EnhancedPrompt, codebaseAnalysis),
                containerId,
                assistant,
                modelPower);

            var finalized = await FinalizePromptAsync(
                context,
                originalPrompt,
                enhanced.EnhancedPrompt,
                codebaseAnalysis,
                researchContext,
                containerId,
                assistant,
                modelPower);

            EnhancedPrompt.Set(context, finalized.EnhancedPrompt);
            CodebaseAnalysis.Set(context, codebaseAnalysis);
            ResearchContext.Set(context, researchContext);
            Rationale.Set(context, finalized.Rationale);

            context.SetVariable("EnhancedPrompt", finalized.EnhancedPrompt);
            context.SetVariable("CodebaseAnalysis", codebaseAnalysis);
            context.SetVariable("ResearchContext", researchContext);
            context.SetVariable("ResearchPrompt", finalized.EnhancedPrompt);

            var verdict = string.Equals(
                originalPrompt.Trim(),
                finalized.EnhancedPrompt.Trim(),
                StringComparison.Ordinal)
                ? "unchanged"
                : "changed";

            context.AddExecutionLogEntry("PromptTransform",
                JsonSerializer.Serialize(new
                {
                    label = "Research Prompt",
                    summary = verdict == "changed"
                        ? "Prompt was enhanced and grounded before downstream execution."
                        : "Research component confirmed the original prompt was already sufficient.",
                    verdict,
                    before = originalPrompt,
                    after = finalized.EnhancedPrompt
                }));

            context.AddExecutionLogEntry("ResearchPromptCompleted",
                JsonSerializer.Serialize(new
                {
                    analysisLength = codebaseAnalysis.Length,
                    researchLength = researchContext.Length,
                    enhancedLength = finalized.EnhancedPrompt.Length
                }));

            await context.CompleteActivityWithOutcomesAsync("Done");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            context.AddExecutionLogEntry("ResearchPromptFailed", ex.ToString());
            EnhancedPrompt.Set(context, originalPrompt);
            CodebaseAnalysis.Set(context, "");
            ResearchContext.Set(context, "");
            Rationale.Set(context, ex.Message);
            await context.CompleteActivityWithOutcomesAsync("Failed");
        }
    }

    internal static string BuildEnhancementInstructions() =>
        """
        You are preparing a coding task for an autonomous engineering agent working inside an existing repository.
        Rewrite the prompt into a precise implementation brief without changing the user's actual intent.
        Preserve constraints, clarify missing detail, and make the task execution-ready.
        """;

    internal static string BuildCodebaseAnalysisPrompt(string originalPrompt, string enhancedPrompt) =>
        $$"""
        You are grounding a coding task in the current repository.

        Analyze the codebase and produce a concise technical brief that covers:
        - the most likely projects, folders, and files involved
        - relevant runtime/framework patterns already used in the repo
        - important constraints or conventions the implementation should respect
        - likely tests or verification steps
        - any uncertainty that should be called out instead of guessed

        ## Original Prompt
        {{originalPrompt}}

        ## Enhanced Prompt
        {{enhancedPrompt}}
        """;

    internal static string BuildResearchContextPrompt(
        string originalPrompt,
        string enhancedPrompt,
        string codebaseAnalysis) =>
        $$"""
        Create a repo-map style execution brief for the task below.

        Focus on:
        - likely files or modules to inspect or edit
        - data flow or architectural touchpoints
        - dependencies or neighboring components that could be affected
        - concrete verification steps to prove the task is complete

        Keep it concise and implementation-focused.

        ## Original Prompt
        {{originalPrompt}}

        ## Enhanced Prompt
        {{enhancedPrompt}}

        ## Codebase Analysis
        {{codebaseAnalysis}}
        """;

    internal static string BuildFinalPrompt(
        string originalPrompt,
        string enhancedPrompt,
        string codebaseAnalysis,
        string researchContext) =>
        $$"""
        Produce one final implementation-ready prompt for a coding agent working inside this repository.

        Requirements for the final prompt:
        - preserve the user's actual intent
        - include grounded repository context only when supported by the analysis
        - name likely files, modules, or subsystems when the evidence is strong
        - include constraints, acceptance criteria, and verification steps
        - call out uncertainty explicitly instead of inventing facts
        - stay concise enough to be practical for downstream execution

        Return JSON only:
        {
          "enhanced_prompt": "<final grounded prompt>",
          "was_enhanced": <true|false>,
          "rationale": "<short explanation of what was improved>"
        }

        ## Original Prompt
        {{originalPrompt}}

        ## Initial Enhanced Prompt
        {{enhancedPrompt}}

        ## Codebase Analysis
        {{codebaseAnalysis}}

        ## Research Context
        {{researchContext}}
        """;

    private static async Task<PromptEnhancementResult> EnhancePromptAsync(
        ActivityExecutionContext context,
        string originalPrompt,
        string containerId,
        string assistant,
        int modelPower)
    {
        var enhancementPrompt = PromptEnhancementActivity.BuildEnhancementPrompt(
            originalPrompt,
            BuildEnhancementInstructions());

        var result = await AiCliExecutor.ExecuteAsync(
            context,
            new AiCliExecutor.ExecutionParams
            {
                ContainerId = containerId,
                AiAssistant = assistant,
                Prompt = enhancementPrompt,
                ModelPower = modelPower,
                OutputSchema = SchemaGenerator.FromType<PromptEnhancementResult>(),
                UseStreaming = false,
                MaxRetries = 2,
                TimeoutMinutes = 10
            });

        var parsed = PromptEnhancementActivity.ParseEnhancementResult(
            result.StructuredOutputJson ?? result.Response,
            originalPrompt);

        context.AddExecutionLogEntry("ResearchPromptStage",
            JsonSerializer.Serialize(new
            {
                stage = "enhance",
                wasEnhanced = parsed.WasEnhanced,
                rationale = parsed.Rationale,
                enhancedLength = parsed.EnhancedPrompt.Length
            }));

        return parsed;
    }

    private static async Task<PromptEnhancementResult> FinalizePromptAsync(
        ActivityExecutionContext context,
        string originalPrompt,
        string enhancedPrompt,
        string codebaseAnalysis,
        string researchContext,
        string containerId,
        string assistant,
        int modelPower)
    {
        var result = await AiCliExecutor.ExecuteAsync(
            context,
            new AiCliExecutor.ExecutionParams
            {
                ContainerId = containerId,
                AiAssistant = assistant,
                Prompt = BuildFinalPrompt(
                    originalPrompt,
                    enhancedPrompt,
                    codebaseAnalysis,
                    researchContext),
                ModelPower = modelPower,
                OutputSchema = SchemaGenerator.FromType<PromptEnhancementResult>(),
                UseStreaming = false,
                MaxRetries = 2,
                TimeoutMinutes = 10
            });

        return PromptEnhancementActivity.ParseEnhancementResult(
            result.StructuredOutputJson ?? result.Response,
            enhancedPrompt);
    }

    private static async Task<string> ExecuteTextStepAsync(
        ActivityExecutionContext context,
        string stageName,
        string prompt,
        string containerId,
        string assistant,
        int modelPower)
    {
        var result = await AiCliExecutor.ExecuteAsync(
            context,
            new AiCliExecutor.ExecutionParams
            {
                ContainerId = containerId,
                AiAssistant = assistant,
                Prompt = prompt,
                ModelPower = modelPower,
                UseStreaming = false,
                MaxRetries = 2,
                TimeoutMinutes = 10
            });

        var response = string.IsNullOrWhiteSpace(result.Response) ? prompt : result.Response.Trim();

        context.AddExecutionLogEntry("ResearchPromptStage",
            JsonSerializer.Serialize(new
            {
                stage = stageName,
                outputLength = response.Length
            }));

        return response;
    }
}
